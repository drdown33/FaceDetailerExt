# Add this to your nodes/__init__.py

import folder_paths
import os
import logging

def setup_ultralytics_integration():
    """Aggressively integrate SwarmUI yolov8 models with Impact Pack"""
    try:
        # Get SwarmUI's yolov8 paths and models
        yolov8_paths = folder_paths.get_folder_paths("yolov8")
        if not yolov8_paths:
            logging.warning("[SwarmUI Ultralytics] No yolov8 paths found")
            return
            
        logging.info(f"[SwarmUI Ultralytics] Found yolov8 paths: {yolov8_paths}")
        
        # Method 1: Add yolov8 paths directly to ultralytics folders
        for yolov8_path in yolov8_paths:
            # Add the yolov8 folder itself to all ultralytics categories
            folder_paths.add_model_folder_path("ultralytics", yolov8_path)
            folder_paths.add_model_folder_path("ultralytics_bbox", yolov8_path)  
            folder_paths.add_model_folder_path("ultralytics_segm", yolov8_path)
            
            # Also try to add subdirectories if they exist
            bbox_subdir = os.path.join(yolov8_path, "bbox")
            segm_subdir = os.path.join(yolov8_path, "segm")
            
            if os.path.exists(bbox_subdir):
                folder_paths.add_model_folder_path("ultralytics_bbox", bbox_subdir)
            if os.path.exists(segm_subdir):
                folder_paths.add_model_folder_path("ultralytics_segm", segm_subdir)
                
            logging.info(f"[SwarmUI Ultralytics] Added {yolov8_path} to ultralytics folders")
        
        # Method 2: Update the folder_names_and_paths directly
        pt_extensions = folder_paths.supported_pt_extensions
        
        for folder_name in ["ultralytics", "ultralytics_bbox", "ultralytics_segm"]:
            if folder_name in folder_paths.folder_names_and_paths:
                current_paths, current_extensions = folder_paths.folder_names_and_paths[folder_name]
                # Add yolov8 paths to existing paths
                updated_paths = list(set(list(current_paths) + yolov8_paths))
                updated_extensions = current_extensions | pt_extensions
                folder_paths.folder_names_and_paths[folder_name] = (updated_paths, updated_extensions)
                logging.info(f"[SwarmUI Ultralytics] Updated {folder_name} paths: {updated_paths}")
            else:
                # Create the folder entry if it doesn't exist
                folder_paths.folder_names_and_paths[folder_name] = (yolov8_paths, pt_extensions)
                logging.info(f"[SwarmUI Ultralytics] Created {folder_name} with paths: {yolov8_paths}")
        
        # Method 3: Also try to register with SwarmUI's model detection
        try:
            # Force refresh the model lists
            if hasattr(folder_paths, 'invalidate_cache'):
                folder_paths.invalidate_cache()
        except:
            pass
            
        # Debug: Print what models are now available
        try:
            ultralytics_models = folder_paths.get_filename_list("ultralytics")
            bbox_models = folder_paths.get_filename_list("ultralytics_bbox") 
            segm_models = folder_paths.get_filename_list("ultralytics_segm")
            
            logging.info(f"[SwarmUI Ultralytics] Available ultralytics models: {ultralytics_models}")
            logging.info(f"[SwarmUI Ultralytics] Available bbox models: {bbox_models}")
            logging.info(f"[SwarmUI Ultralytics] Available segm models: {segm_models}")
        except Exception as e:
            logging.error(f"[SwarmUI Ultralytics] Error listing models: {e}")
            
    except Exception as e:
        logging.error(f"[SwarmUI Ultralytics] Failed to setup integration: {e}")

# Call this immediately
setup_ultralytics_integration()

# Import the other nodes after setup
from .sDetailerNode import NODE_CLASS_MAPPINGS as NODE_CLASS_MAPPINGS_DETECTOR
from .sDetailerInpaint import NODE_CLASS_MAPPINGS as NODE_CLASS_MAPPINGS_INPAINT
from .sDetailerNode import NODE_DISPLAY_NAME_MAPPINGS as NODE_DISPLAY_NAME_MAPPINGS_DETECTOR  
from .sDetailerInpaint import NODE_DISPLAY_NAME_MAPPINGS as NODE_DISPLAY_NAME_MAPPINGS_INPAINT

NODE_CLASS_MAPPINGS = {**NODE_CLASS_MAPPINGS_DETECTOR, **NODE_CLASS_MAPPINGS_INPAINT}
NODE_DISPLAY_NAME_MAPPINGS = {**NODE_DISPLAY_NAME_MAPPINGS_DETECTOR, **NODE_DISPLAY_NAME_MAPPINGS_INPAINT}

print("sDetailer nodes module initialized with SwarmUI yolov8 integration")
