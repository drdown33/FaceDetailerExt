import os
import folder_paths
import logging
import torch
import numpy as np
from PIL import Image
import cv2
from impact.core import SEG
from impact.utils import *

# Import the subcore's load_yolo function which handles PyTorch 2.6+ security properly
try:
    # Try to import from the Impact Pack subcore if available
    from impact.impact_pack.impact_subpack import subcore
    load_yolo_model = subcore.load_yolo
    logging.info("[YoloDetectorProvider] Using Impact Pack subcore's load_yolo function")
except ImportError:
    # Fallback to our own implementation with comprehensive safe globals
    logging.warning("[YoloDetectorProvider] Impact Pack subcore not available, using fallback")
    
    def load_yolo_model(model_path):
        """Load YOLO model using ultralytics with PyTorch 2.6 compatibility"""
        try:
            from ultralytics import YOLO
            
            # Use the same comprehensive approach as Impact Pack subcore
            if hasattr(torch.serialization, 'safe_globals'):
                # Import all necessary modules
                import ultralytics.nn.tasks
                import ultralytics.nn.modules.conv
                import ultralytics.nn.modules.block
                import ultralytics.nn.modules.head
                import ultralytics.nn.modules as modules
                import ultralytics.nn.modules.block as block_modules
                import torch.nn.modules as torch_modules
                import ultralytics.utils.loss as loss_modules
                from ultralytics.utils import IterableSimpleNamespace
                from ultralytics.utils.tal import TaskAlignedAssigner
                import inspect
                
                # Build comprehensive whitelist like Impact Pack does
                torch_whitelist = []
                
                # Add all ultralytics.nn.modules classes
                for name, obj in inspect.getmembers(modules):
                    if inspect.isclass(obj) and obj.__module__.startswith("ultralytics.nn.modules"):
                        aliasObj = type(name, (obj,), {})
                        aliasObj.__module__ = "ultralytics.nn.modules"
                        torch_whitelist.append(obj)
                        torch_whitelist.append(aliasObj)

                for name, obj in inspect.getmembers(block_modules):
                    if inspect.isclass(obj) and obj.__module__.startswith("ultralytics.nn.modules"):
                        aliasObj = type(name, (obj,), {})
                        aliasObj.__module__ = "ultralytics.nn.modules.block"
                        torch_whitelist.append(obj)
                        torch_whitelist.append(aliasObj)

                for name, obj in inspect.getmembers(loss_modules):
                    if inspect.isclass(obj) and obj.__module__.startswith("ultralytics.utils.loss"):
                        aliasObj = type(name, (obj,), {})
                        aliasObj.__module__ = "ultralytics.yolo.utils.loss"
                        torch_whitelist.append(obj)
                        torch_whitelist.append(aliasObj)

                for name, obj in inspect.getmembers(torch_modules):
                    if inspect.isclass(obj) and obj.__module__.startswith("torch.nn.modules"):
                        torch_whitelist.append(obj)
                
                # Add specific classes that might be missing
                torch_whitelist.extend([
                    ultralytics.nn.tasks.DetectionModel,
                    ultralytics.nn.tasks.SegmentationModel,
                    IterableSimpleNamespace,
                    TaskAlignedAssigner,
                ])
                
                # Add aliases for backward compatibility
                aliasIterableSimpleNamespace = type("IterableSimpleNamespace", (IterableSimpleNamespace,), {})
                aliasIterableSimpleNamespace.__module__ = "ultralytics.yolo.utils"
                
                aliasTaskAlignedAssigner = type("TaskAlignedAssigner", (TaskAlignedAssigner,), {})
                aliasTaskAlignedAssigner.__module__ = "ultralytics.yolo.utils.tal"
                
                torch_whitelist.extend([aliasIterableSimpleNamespace, aliasTaskAlignedAssigner])
                
                with torch.serialization.safe_globals(torch_whitelist):
                    try:
                        return YOLO(model_path)
                    except ModuleNotFoundError:
                        # https://github.com/ultralytics/ultralytics/issues/3856
                        YOLO("yolov8n.pt")
                        return YOLO(model_path)
            else:
                # Older PyTorch versions
                try:
                    return YOLO(model_path)
                except ModuleNotFoundError:
                    YOLO("yolov8n.pt")
                    return YOLO(model_path)
                    
        except ImportError:
            raise ImportError("ultralytics package is required. Install with: pip install ultralytics")


def create_segmasks(results):
    """Convert detection results to segmask format expected by Impact Pack"""
    labels = results[0] if len(results) > 0 else []
    bboxs = results[1] if len(results) > 1 else []
    segms = results[2] if len(results) > 2 else []
    confidence = results[3] if len(results) > 3 else []

    results = []
    for i in range(len(segms)):
        item = (bboxs[i], segms[i].astype(np.float32), confidence[i])
        results.append(item)
    return results


class YoloBBoxDetector:
    def __init__(self, model):
        self.model = model

    def detect(self, image, threshold, dilation, crop_factor, drop_size=1, detailer_hook=None):
        drop_size = max(drop_size, 1)
        
        # Convert ComfyUI image tensor to format expected by YOLO
        if isinstance(image, torch.Tensor):
            img_array = (image.squeeze().cpu().numpy() * 255).astype(np.uint8)
            if img_array.ndim == 3 and img_array.shape[2] == 3:
                pil_image = Image.fromarray(img_array)
            else:
                raise ValueError(f"Unexpected image format: {img_array.shape}")
        else:
            pil_image = image

        # Run YOLO detection
        results = self.model.predict(pil_image, conf=threshold, verbose=False)
        
        # Convert to Impact Pack format
        if results and len(results) > 0 and results[0].boxes is not None:
            boxes = results[0].boxes.xyxy.cpu().numpy()  # [x1, y1, x2, y2] format
            confidences = results[0].boxes.conf.cpu().numpy()
            class_ids = results[0].boxes.cls.cpu().numpy()
            
            # Create masks from bounding boxes
            img_height, img_width = img_array.shape[:2]
            
            labels = []
            bboxs = []
            segms = []
            scores = []
            
            for i, box in enumerate(boxes):
                x1, y1, x2, y2 = box
                area = (x2 - x1) * (y2 - y1)
                
                if area >= drop_size:
                    # Create rectangular mask from bounding box
                    mask = np.zeros((img_height, img_width), dtype=np.float32)
                    mask[int(y1):int(y2), int(x1):int(x2)] = 1.0
                    
                    labels.append("A")  # Generic label like Impact Pack's BBoxDetector
                    bboxs.append([y1, x1, y2, x2])  # Convert to y1,x1,y2,x2 format like Impact Pack
                    segms.append(mask)
                    scores.append(confidences[i])
            
            mmdet_results = [labels, bboxs, segms, scores]
        else:
            mmdet_results = [[], [], [], []]
        
        segmasks = create_segmasks(mmdet_results)

        if dilation != 0:
            segmasks = dilate_masks(segmasks, dilation)

        items = []
        h = image.shape[1]
        w = image.shape[2]

        for x in segmasks:
            item_bbox = x[0]
            item_mask = x[1]
            confidence = x[2]

            y1, x1, y2, x2 = item_bbox

            if x2 - x1 > drop_size and y2 - y1 > drop_size:
                crop_region = make_crop_region(w, h, item_bbox, crop_factor)
                
                if detailer_hook is not None:
                    crop_region = detailer_hook.post_crop_region(w, h, item_bbox, crop_region)

                cropped_image = crop_image(image, crop_region)
                cropped_mask = crop_ndarray2(item_mask, crop_region)

                item = SEG(cropped_image, cropped_mask, confidence, crop_region, item_bbox, "A", None)
                items.append(item)

        shape = h, w
        segs = shape, items

        if detailer_hook is not None and hasattr(detailer_hook, "post_detection"):
            segs = detailer_hook.post_detection(segs)

        return segs

    def detect_combined(self, image, threshold, dilation):
        segs = self.detect(image, threshold, dilation, 1.0, 1)
        from impact.core import segs_to_combined_mask
        return segs_to_combined_mask(segs)

    def setAux(self, x):
        pass


class YoloSegmDetector:
    def __init__(self, model):
        self.model = model

    def detect(self, image, threshold, dilation, crop_factor, drop_size=1, detailer_hook=None):
        drop_size = max(drop_size, 1)
        
        # Convert ComfyUI image tensor to format expected by YOLO
        if isinstance(image, torch.Tensor):
            img_array = (image.squeeze().cpu().numpy() * 255).astype(np.uint8)
            if img_array.ndim == 3 and img_array.shape[2] == 3:
                pil_image = Image.fromarray(img_array)
            else:
                raise ValueError(f"Unexpected image format: {img_array.shape}")
        else:
            pil_image = image

        # Run YOLO segmentation
        results = self.model.predict(pil_image, conf=threshold, verbose=False)
        
        # Convert to Impact Pack format
        if results and len(results) > 0:
            if hasattr(results[0], 'masks') and results[0].masks is not None:
                # Segmentation model - use actual masks
                masks = results[0].masks.data.cpu().numpy()  # [N, H, W]
                boxes = results[0].boxes.xyxy.cpu().numpy()  # [x1, y1, x2, y2] format
                confidences = results[0].boxes.conf.cpu().numpy()
                class_ids = results[0].boxes.cls.cpu().numpy()
                
                labels = []
                bboxs = []
                segms = []
                scores = []
                
                for i, mask in enumerate(masks):
                    mask_area = np.sum(mask > 0.5)
                    if mask_area >= drop_size:
                        x1, y1, x2, y2 = boxes[i]
                        
                        labels.append("A")  # Generic label
                        bboxs.append([y1, x1, y2, x2])  # Convert to y1,x1,y2,x2 format like Impact Pack
                        segms.append(mask.astype(np.float32))
                        scores.append(confidences[i])
                
                mmdet_results = [labels, bboxs, segms, scores]
            
            elif hasattr(results[0], 'boxes') and results[0].boxes is not None:
                # Detection model - create rectangular masks from bounding boxes
                boxes = results[0].boxes.xyxy.cpu().numpy()
                confidences = results[0].boxes.conf.cpu().numpy()
                class_ids = results[0].boxes.cls.cpu().numpy()
                
                img_height, img_width = img_array.shape[:2]
                
                labels = []
                bboxs = []
                segms = []
                scores = []
                
                for i, box in enumerate(boxes):
                    x1, y1, x2, y2 = box
                    area = (x2 - x1) * (y2 - y1)
                    if area >= drop_size:
                        # Create rectangular mask from bounding box
                        mask = np.zeros((img_height, img_width), dtype=np.float32)
                        mask[int(y1):int(y2), int(x1):int(x2)] = 1.0
                        
                        labels.append("A")
                        bboxs.append([y1, x1, y2, x2])  # Convert to y1,x1,y2,x2 format like Impact Pack
                        segms.append(mask)
                        scores.append(confidences[i])
                
                mmdet_results = [labels, bboxs, segms, scores]
            else:
                mmdet_results = [[], [], [], []]
        else:
            mmdet_results = [[], [], [], []]
        
        segmasks = create_segmasks(mmdet_results)

        if dilation != 0:
            segmasks = dilate_masks(segmasks, dilation)

        items = []
        h = image.shape[1]
        w = image.shape[2]
        
        for x in segmasks:
            item_bbox = x[0]
            item_mask = x[1]
            confidence = x[2]

            y1, x1, y2, x2 = item_bbox

            if x2 - x1 > drop_size and y2 - y1 > drop_size:
                crop_region = make_crop_region(w, h, item_bbox, crop_factor)
                
                if detailer_hook is not None:
                    crop_region = detailer_hook.post_crop_region(w, h, item_bbox, crop_region)

                cropped_image = crop_image(image, crop_region)
                cropped_mask = crop_ndarray2(item_mask, crop_region)

                item = SEG(cropped_image, cropped_mask, confidence, crop_region, item_bbox, "A", None)
                items.append(item)

        shape = h, w
        segs = shape, items

        if detailer_hook is not None and hasattr(detailer_hook, "post_detection"):
            segs = detailer_hook.post_detection(segs)

        return segs

    def detect_combined(self, image, threshold, dilation):
        segs = self.detect(image, threshold, dilation, 1.0, 1)
        from impact.core import segs_to_combined_mask
        return segs_to_combined_mask(segs)

    def setAux(self, x):
        pass


class YoloDetectorProvider:
    @classmethod
    def INPUT_TYPES(s):
        # Get models from the same location as SwarmYoloDetection (yolov8 folder)
        models = folder_paths.get_filename_list("yolov8")
        return {"required": {"model_name": (models, )}}
    
    RETURN_TYPES = ("BBOX_DETECTOR", "SEGM_DETECTOR")
    FUNCTION = "doit"

    CATEGORY = "ImpactPack"

    def doit(self, model_name):
        # Get the full path using the same method as SwarmYoloDetection
        model_path = folder_paths.get_full_path("yolov8", model_name)

        if model_path is None:
            logging.error(f"[YoloDetectorProvider] model file '{model_name}' is not found in yolov8 directory:")
            
            yolov8_paths = folder_paths.get_folder_paths("yolov8")
            formatted_paths = "\n\t".join(yolov8_paths)
            logging.error(f'\t{formatted_paths}\n')

            raise ValueError(f"[YoloDetectorProvider] model file '{model_name}' is not found.")

        model = load_yolo_model(model_path)

        # Return both bbox and segm detectors for all models
        # The actual functionality will depend on the model's capabilities
        return YoloBBoxDetector(model), YoloSegmDetector(model)


NODE_CLASS_MAPPINGS = {
    "YoloDetectorProvider": YoloDetectorProvider
}


NODE_DISPLAY_NAME_MAPPINGS = {
    "YoloDetectorProvider": "YOLO Detector Provider"
}
