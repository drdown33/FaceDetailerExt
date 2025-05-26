import os
import folder_paths
import logging
import torch
import numpy as np
from PIL import Image


# Standalone detector classes that implement the Impact Pack interface
class YoloBBoxDetector:
    def __init__(self, model):
        self.model = model

    def detect(self, image, threshold, dilation, crop_factor, drop_size, detailer_hook=None):
        # Convert ComfyUI image tensor to PIL Image
        if isinstance(image, torch.Tensor):
            # Assume image is in format [batch, height, width, channels] and normalized 0-1
            img_array = (image.squeeze().cpu().numpy() * 255).astype(np.uint8)
            if img_array.ndim == 3 and img_array.shape[2] == 3:
                pil_image = Image.fromarray(img_array)
            else:
                raise ValueError(f"Unexpected image format: {img_array.shape}")
        else:
            pil_image = image

        # Run YOLO detection
        results = self.model.predict(pil_image, conf=threshold, verbose=False)
        
        # Extract bounding boxes
        if results and len(results) > 0 and results[0].boxes is not None:
            boxes = results[0].boxes.xyxy.cpu().numpy()  # [x1, y1, x2, y2] format
            confidences = results[0].boxes.conf.cpu().numpy()
            class_ids = results[0].boxes.cls.cpu().numpy()
            
            # Filter by drop_size (minimum box area)
            filtered_boxes = []
            for i, box in enumerate(boxes):
                x1, y1, x2, y2 = box
                area = (x2 - x1) * (y2 - y1)
                if area >= drop_size:
                    filtered_boxes.append({
                        'bbox': box,
                        'confidence': confidences[i],
                        'class_id': class_ids[i]
                    })
            
            return filtered_boxes
        else:
            return []


class YoloSegmDetector:
    def __init__(self, model):
        self.model = model

    def detect(self, image, threshold, dilation, crop_factor, drop_size, detailer_hook=None):
        # Convert ComfyUI image tensor to PIL Image
        if isinstance(image, torch.Tensor):
            # Assume image is in format [batch, height, width, channels] and normalized 0-1
            img_array = (image.squeeze().cpu().numpy() * 255).astype(np.uint8)
            if img_array.ndim == 3 and img_array.shape[2] == 3:
                pil_image = Image.fromarray(img_array)
            else:
                raise ValueError(f"Unexpected image format: {img_array.shape}")
        else:
            pil_image = image

        # Run YOLO segmentation
        results = self.model.predict(pil_image, conf=threshold, verbose=False)
        
        # Extract segmentation masks and create SEGS format
        segs = []
        if results and len(results) > 0:
            if hasattr(results[0], 'masks') and results[0].masks is not None:
                masks = results[0].masks.data.cpu().numpy()  # [N, H, W]
                boxes = results[0].boxes.xyxy.cpu().numpy()  # [x1, y1, x2, y2] format
                confidences = results[0].boxes.conf.cpu().numpy()
                class_ids = results[0].boxes.cls.cpu().numpy()
                
                for i, mask in enumerate(masks):
                    # Apply dilation if specified
                    if dilation != 0:
                        import cv2
                        kernel = np.ones((abs(dilation), abs(dilation)), np.uint8)
                        if dilation > 0:
                            mask = cv2.dilate(mask.astype(np.uint8), kernel, iterations=1).astype(np.float32)
                        else:
                            mask = cv2.erode(mask.astype(np.uint8), kernel, iterations=1).astype(np.float32)
                    
                    # Check if mask area meets drop_size requirement
                    mask_area = np.sum(mask > 0.5)
                    if mask_area >= drop_size:
                        x1, y1, x2, y2 = boxes[i]
                        
                        # Create SEGS format (this is a simplified version)
                        seg = {
                            'mask': torch.from_numpy(mask),
                            'bbox': (int(x1), int(y1), int(x2), int(y2)),
                            'confidence': float(confidences[i]),
                            'class_id': int(class_ids[i]),
                            'crop_region': (int(x1), int(y1), int(x2), int(y2))
                        }
                        segs.append(seg)
            
            elif hasattr(results[0], 'boxes') and results[0].boxes is not None:
                # Fallback to bounding boxes if no segmentation masks
                boxes = results[0].boxes.xyxy.cpu().numpy()
                confidences = results[0].boxes.conf.cpu().numpy()
                class_ids = results[0].boxes.cls.cpu().numpy()
                
                img_height, img_width = image.shape[1:3] if isinstance(image, torch.Tensor) else (pil_image.height, pil_image.width)
                
                for i, box in enumerate(boxes):
                    x1, y1, x2, y2 = box
                    area = (x2 - x1) * (y2 - y1)
                    if area >= drop_size:
                        # Create a rectangular mask from bounding box
                        mask = np.zeros((img_height, img_width), dtype=np.float32)
                        mask[int(y1):int(y2), int(x1):int(x2)] = 1.0
                        
                        seg = {
                            'mask': torch.from_numpy(mask),
                            'bbox': (int(x1), int(y1), int(x2), int(y2)),
                            'confidence': float(confidences[i]),
                            'class_id': int(class_ids[i]),
                            'crop_region': (int(x1), int(y1), int(x2), int(y2))
                        }
                        segs.append(seg)
        
        return segs


class NoSegmDetector:
    def __init__(self):
        pass
    
    def detect(self, image, threshold, dilation, crop_factor, drop_size, detailer_hook=None):
        return []


def load_yolo_model(model_path):
    """Load YOLO model using ultralytics with PyTorch 2.6 compatibility"""
    try:
        import torch
        from ultralytics import YOLO
        
        # Handle PyTorch 2.6+ weights_only security restrictions
        try:
            # Add more comprehensive safe globals
            if hasattr(torch.serialization, 'add_safe_globals'):
                import ultralytics.nn.tasks
                import torch.nn.modules.container
                import torch.nn.modules.conv
                import torch.nn.modules.batchnorm
                import torch.nn.modules.activation
                
                safe_classes = [
                    ultralytics.nn.tasks.SegmentationModel,
                    ultralytics.nn.tasks.DetectionModel,
                    ultralytics.nn.tasks.ClassificationModel,
                    ultralytics.nn.tasks.PoseModel,
                    ultralytics.nn.tasks.OBBModel,
                    torch.nn.modules.container.Sequential,
                    torch.nn.modules.container.ModuleList,
                    torch.nn.modules.conv.Conv2d,
                    torch.nn.modules.batchnorm.BatchNorm2d,
                    torch.nn.modules.activation.SiLU
                ]
                torch.serialization.add_safe_globals(safe_classes)
        except (AttributeError, ImportError) as e:
            logging.debug(f"Could not add safe globals: {e}")
        
        # Try loading with safe_globals context manager if available
        try:
            if hasattr(torch.serialization, 'safe_globals'):
                import ultralytics.nn.tasks
                import torch.nn.modules.container
                import torch.nn.modules.conv
                import torch.nn.modules.batchnorm
                import torch.nn.modules.activation
                
                safe_classes = [
                    ultralytics.nn.tasks.SegmentationModel,
                    ultralytics.nn.tasks.DetectionModel,
                    ultralytics.nn.tasks.ClassificationModel,
                    ultralytics.nn.tasks.PoseModel,
                    ultralytics.nn.tasks.OBBModel,
                    torch.nn.modules.container.Sequential,
                    torch.nn.modules.container.ModuleList,
                    torch.nn.modules.conv.Conv2d,
                    torch.nn.modules.batchnorm.BatchNorm2d,
                    torch.nn.modules.activation.SiLU
                ]
                
                with torch.serialization.safe_globals(safe_classes):
                    return YOLO(model_path)
            else:
                return YOLO(model_path)
        except Exception as e:
            # If safe loading fails, try with the legacy approach
            logging.warning(f"Safe loading failed, attempting legacy load: {e}")
            
            # Temporarily patch torch.load to use weights_only=False
            original_load = torch.load
            def patched_load(*args, **kwargs):
                if 'weights_only' not in kwargs:
                    kwargs['weights_only'] = False
                return original_load(*args, **kwargs)
            
            try:
                torch.load = patched_load
                return YOLO(model_path)
            finally:
                torch.load = original_load
                
    except ImportError:
        raise ImportError("ultralytics package is required. Install with: pip install ultralytics")


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
