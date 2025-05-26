import os
import folder_paths
from . import subcore
import logging


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

        model = subcore.load_yolo(model_path)

        # Return both bbox and segm detectors for all models
        # The actual functionality will depend on the model's capabilities
        return subcore.UltraBBoxDetector(model), subcore.UltraSegmDetector(model)


NODE_CLASS_MAPPINGS = {
    "YoloDetectorProvider": YoloDetectorProvider
}


NODE_DISPLAY_NAME_MAPPINGS = {
    "YoloDetectorProvider": "YOLO Detector Provider"
}
