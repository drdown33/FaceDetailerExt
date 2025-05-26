# Add this to your extension's __init__.py or main module file

import folder_paths
import os
import logging

def patch_ultralytics_paths():
    """Patch the Impact Pack's ultralytics paths to include SwarmUI's yolov8 folder"""
    try:
        # Get the yolov8 folder paths from SwarmUI
        yolov8_paths = folder_paths.get_folder_paths("yolov8")
        
        # Add these paths to the ultralytics folder paths
        for yolov8_path in yolov8_paths:
            # Add to ultralytics_bbox
            folder_paths.add_model_folder_path("ultralytics_bbox", os.path.join(yolov8_path, "bbox"))
            folder_paths.add_model_folder_path("ultralytics_bbox", yolov8_path)  # Also add root yolov8 folder
            
            # Add to ultralytics_segm  
            folder_paths.add_model_folder_path("ultralytics_segm", os.path.join(yolov8_path, "segm"))
            folder_paths.add_model_folder_path("ultralytics_segm", yolov8_path)  # Also add root yolov8 folder
            
            # Add to general ultralytics
            folder_paths.add_model_folder_path("ultralytics", yolov8_path)
            
            logging.info(f"[SwarmUI Ultralytics] Added yolov8 path to ultralytics folders: {yolov8_path}")
            
        # Update extensions for all ultralytics folders
        pt_extensions = folder_paths.supported_pt_extensions
        
        for folder_name in ["ultralytics_bbox", "ultralytics_segm", "ultralytics"]:
            if folder_name in folder_paths.folder_names_and_paths:
                current_paths, current_extensions = folder_paths.folder_names_and_paths[folder_name]
                updated_extensions = current_extensions | pt_extensions
                folder_paths.folder_names_and_paths[folder_name] = (current_paths, updated_extensions)
        
        logging.info("[SwarmUI Ultralytics] Successfully patched ultralytics model paths")
        
    except Exception as e:
        logging.error(f"[SwarmUI Ultralytics] Failed to patch ultralytics paths: {e}")

# Call this during your extension initialization
patch_ultralytics_paths()
