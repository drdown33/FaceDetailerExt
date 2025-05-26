import os
import folder_paths
import logging


# Standalone detector classes that mimic the Impact Pack's functionality
class YoloBBoxDetector:
    def __init__(self, model):
        self.model = model

class YoloSegmDetector:
    def __init__(self, model):
        self.model = model

class NoSegmDetector:
    def __init__(self):
        pass


def load_yolo_model(model_path):
    """Load YOLO model using ultralytics with PyTorch 2.6 compatibility"""
    try:
        import torch
        from ultralytics import YOLO
        
        # Handle PyTorch 2.6+ weights_only security restrictions
        try:
            # Try to add safe globals for ultralytics models
            if hasattr(torch.serialization, 'add_safe_globals'):
                import ultralytics.nn.tasks
                torch.serialization.add_safe_globals([
                    ultralytics.nn.tasks.SegmentationModel,
                    ultralytics.nn.tasks.DetectionModel,
                    ultralytics.nn.tasks.ClassificationModel,
                    ultralytics.nn.tasks.PoseModel,
                    ultralytics.nn.tasks.OBBModel
                ])
        except (AttributeError, ImportError):
            # Fallback for older PyTorch versions or if modules don't exist
            pass
        
        # Try loading with safe_globals context manager if available
        try:
            if hasattr(torch.serialization, 'safe_globals'):
                import ultralytics.nn.tasks
                with torch.serialization.safe_globals([
                    ultralytics.nn.tasks.SegmentationModel,
                    ultralytics.nn.tasks.DetectionModel,
                    ultralytics.nn.tasks.ClassificationModel,
                    ultralytics.nn.tasks.PoseModel,
                    ultralytics.nn.tasks.OBBModel
                ]):
                    return YOLO(model_path)
            else:
                return YOLO(model_path)
        except Exception as e:
            # If safe loading fails, try with the legacy approach
            # This is less secure but may be necessary for some models
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
