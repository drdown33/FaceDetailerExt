using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.Text2Image;
using SwarmUI.Builtin_ComfyUIBackend;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO; // Keep for FilePath if OnPreInit were to be used for other reasons
using System;
using System.Linq;
using SwarmUI.Accounts; // Keep if GetValues uses session
using FreneticUtilities.FreneticExtensions; // Keep for general utilities like TryGet

// Note: No longer importing SDetailer-specific node files as they are removed.

namespace MaskDetailerExtension // Namespace can remain or be changed if you prefer
{
    public class MaskDetailerExtension : Extension
    {
        // New group for MaskDetailer parameters
        public static T2IParamGroup MaskDetailerGroup = new("MaskDetailer", "MaskDetailer Workflow Options", Toggles: true, Open: false, OrderPriority: -1); // Or any other priority

        // Parameters for UltralyticsDetectorProvider
        public static T2IRegisteredParam<string> MD_DetectionModel;

        // Parameters for SegmDetectorCombined_v2
        public static T2IRegisteredParam<float> MD_ConfidenceThreshold;
        public static T2IRegisteredParam<int> MD_MaskDilation;

        // Parameters for MaskDetailerPipe
        public static T2IRegisteredParam<float> MD_GuideSize;
        public static T2IRegisteredParam<float> MD_MaxSize;
        public static T2IRegisteredParam<int> MD_Steps; // Override
        public static T2IRegisteredParam<float> MD_CFGScale; // Override
        public static T2IRegisteredParam<string> MD_Sampler; // Override
        public static T2IRegisteredParam<string> MD_Scheduler; // Override
        public static T2IRegisteredParam<long> MD_Seed; // Override
        public static T2IRegisteredParam<float> MD_Denoise;
        public static T2IRegisteredParam<float> MD_CropFactor;
        public static T2IRegisteredParam<int> MD_DropSize;
        // public static T2IRegisteredParam<T2IModel> MD_Checkpoint; // Removed, basic_pipe handles this
        // public static T2IRegisteredParam<T2IModel> MD_VAE; // Removed, basic_pipe handles this
        // public static T2IRegisteredParam<string> MD_Prompt; // Removed, basic_pipe handles this
        // public static T2IRegisteredParam<string> MD_NegativePrompt; // Removed, basic_pipe handles this


        // OnPreInit is removed as we are no longer adding custom node paths for sDetailer's python nodes.
        // If this extension had other reasons to use OnPreInit (like loading other resources), it could be kept.
        // For now, assuming Impact Pack nodes are globally available to ComfyUI.
        /*
        public override void OnPreInit()
        {
            // string nodeFolder = Path.Join(FilePath, "nodes"); // This was for sDetailer's python nodes
            // ComfyUISelfStartBackend.CustomNodePaths.Add(nodeFolder);
            // Logs.Init($"Adding {nodeFolder} to CustomNodePaths");
        }
        */

        public override void OnInit()
        {
            // Remove old NodeToFeatureMap entries for SDetailerDetect and SDetailerInpaintHelper
            // ComfyUIBackendExtension.NodeToFeatureMap.Remove("SDetailerDetect");
            // ComfyUIBackendExtension.NodeToFeatureMap.Remove("SDetailerInpaintHelper");
            // The new nodes (ToBasicPipe, etc.) are assumed to be part of a known pack (Impact Pack)
            // and may not need explicit mapping here if they don't require special feature flags.

            // --- Parameters for UltralyticsDetectorProvider ---
            MD_DetectionModel = T2IParamTypes.Register<string>(new(
                "MD Detection Model",
                "Select detection model for MaskDetailer (e.g., segmentation models like 'segm/model.pt'). Models should be in the appropriate ComfyUI custom node model paths (eg. ComfyUI/models/ultralytics/segm).",
                "(None)", // Default
                Toggleable: false, // This being (None) can be the trigger to disable the workflow
                Group: MaskDetailerGroup,
                FeatureFlag: "comfyui",
                ID: "maskdetailer_detection_model",
                OrderPriority: 10,
                GetValues: (session) => {
                    // TODO: Implement robust logic to list available segmentation models
                    // This might involve a new API call to your backend or a predefined list.
                    // The ComfyUI API endpoint /object_info can list nodes, and then you could parse UltralyticsDetectorProvider
                    // for its model widget options if it's a combo. Or, scan specific model directories.
                    // For now, using a placeholder list based on previous examples.
                    var exampleModels = new List<string> { "(None)", "segm/99coins_anime_girl_face_m_seg.pt", "segm/bbox/face_yolov8m.pt" };
                    // Potentially fetch from backend:
                    // var models = ComfyUIBackendExtension.GetModels("ultralytics/segm") ?? exampleModels;
                    // return models.Count > 0 ? models : exampleModels;
                    return exampleModels;
                }
            ));

            // --- Parameters for SegmDetectorCombined_v2 ---
            MD_ConfidenceThreshold = T2IParamTypes.Register<float>(new(
                "MD Confidence Threshold",
                "Minimum detection score (0-1) for SegmDetector. Lower = more detections.",
                0.3f, // Default from SegmDetectorCombined_v2 in JSON was 0.3
                Min: 0.0f, Max: 1.0f, Step: 0.01f, // Finer step
                Toggleable: false,
                Group: MaskDetailerGroup,
                FeatureFlag: "comfyui",
                ID: "maskdetailer_confidence_threshold",
                OrderPriority: 20
            ));

            MD_MaskDilation = T2IParamTypes.Register<int>(new(
                "MD Mask Dilation",
                "Dilation pixels for SegmDetector. Positive values expand the mask. 0 for no change.",
                0, // Default from SegmDetectorCombined_v2 in JSON was 0
                Min: 0, Max: 128, Step: 1, // Adjusted range
                Toggleable: false,
                Group: MaskDetailerGroup,
                FeatureFlag: "comfyui",
                ID: "maskdetailer_mask_dilation",
                OrderPriority: 30
            ));

            // --- Parameters for MaskDetailerPipe ---
            MD_GuideSize = T2IParamTypes.Register<float>(new(
                "MD Guide Size",
                "Target size for the detailer to operate on (pixels).",
                512f, Min: 64f, Max: 16384f, Step: 64f, // nodes.MAX_RESOLUTION equivalent, common step
                Toggleable: false, Group: MaskDetailerGroup, FeatureFlag: "comfyui",
                ID: "maskdetailer_guide_size", OrderPriority: 40
            ));

            MD_MaxSize = T2IParamTypes.Register<float>(new(
                "MD Max Size",
                "Maximum size constraint for the detailing process (pixels).",
                1024f, Min: 64f, Max: 16384f, Step: 64f, // nodes.MAX_RESOLUTION equivalent
                Toggleable: false, Group: MaskDetailerGroup, FeatureFlag: "comfyui",
                ID: "maskdetailer_max_size", OrderPriority: 50
            ));

            MD_Steps = T2IParamTypes.Register<int>(new(
                "MD Steps (Override)",
                "Override sampling steps for MaskDetailer. Uses main steps if not enabled.",
                20, Min: 1, Max: 10000, // Default, Min, Max from MaskDetailerPipe INPUT_TYPES
                Toggleable: true, Group: MaskDetailerGroup, FeatureFlag: "comfyui",
                ID: "maskdetailer_steps", OrderPriority: 60
            ));

            MD_CFGScale = T2IParamTypes.Register<float>(new(
                "MD CFG Scale (Override)",
                "Override CFG Scale for MaskDetailer. Uses main CFG if not enabled.",
                8.0f, Min: 0.0f, Max: 100.0f, Step: 0.1f, // Default, Min, Max from MaskDetailerPipe INPUT_TYPES, finer step
                Toggleable: true, Group: MaskDetailerGroup, FeatureFlag: "comfyui",
                ID: "maskdetailer_cfg_scale", OrderPriority: 70
            ));

            MD_Sampler = T2IParamTypes.Register<string>(new(
                "MD Sampler (Override)",
                "Override sampler for MaskDetailer. Uses main sampler if not enabled.",
                null, // Will use default from MaskDetailerPipe or main if not set
                Toggleable: true, Group: MaskDetailerGroup, FeatureFlag: "comfyui",
                ID: "maskdetailer_sampler", OrderPriority: 80,
                GetValues: (session) => {
                    T2IParamType samplerType = ComfyUIBackendExtension.SamplerParam?.Type;
                    if (samplerType?.GetValues != null) { try { return samplerType.GetValues(session); } catch { } }
                    return new List<string>(ComfyUIBackendExtension.SamplersDefault); // Fallback
                }
            ));

            MD_Scheduler = T2IParamTypes.Register<string>(new(
                "MD Scheduler (Override)",
                "Override scheduler for MaskDetailer. Uses main scheduler if not enabled.",
                null, // Will use default from MaskDetailerPipe or main if not set
                Toggleable: true, Group: MaskDetailerGroup, FeatureFlag: "comfyui",
                ID: "maskdetailer_scheduler", OrderPriority: 90,
                GetValues: (session) => {
                    T2IParamType schedulerType = ComfyUIBackendExtension.SchedulerParam?.Type;
                    if (schedulerType?.GetValues != null) { try { return schedulerType.GetValues(session); } catch { } }
                    return new List<string>(ComfyUIBackendExtension.SchedulersDefault); // Fallback
                }
            ));
            
            MD_Seed = T2IParamTypes.Register<long>(new(
                "MD Seed (Override)",
                "MaskDetailer seed. Uses main image seed if not enabled. Set to 0 for MaskDetailer's internal random.",
                0, Min: -1, Max: long.MaxValue, // -1 could mean use main seed, 0 for MaskDetailerPipe default (random each time)
                Toggleable: true, Group: MaskDetailerGroup, FeatureFlag: "comfyui",
                ID: "maskdetailer_seed", OrderPriority: 100
            ));

            MD_Denoise = T2IParamTypes.Register<float>(new(
                "MD Denoise Strength",
                "Denoising strength for the MaskDetailer process (0 = none, 1 = full replace).",
                0.5f, Min: 0.0f, Max: 1.0f, Step: 0.01f, // From MaskDetailerPipe INPUT_TYPES
                Toggleable: false, Group: MaskDetailerGroup, FeatureFlag: "comfyui",
                ID: "maskdetailer_denoise", OrderPriority: 110
            ));

            MD_CropFactor = T2IParamTypes.Register<float>(new(
                "MD Crop Factor",
                "Factor to expand the bounding box for cropping before detailing (1.0 = no expansion).",
                3.0f, Min: 1.0f, Max: 10f, Step: 0.1f, // From MaskDetailerPipe INPUT_TYPES
                Toggleable: false, Group: MaskDetailerGroup, FeatureFlag: "comfyui",
                ID: "maskdetailer_crop_factor", OrderPriority: 120
            ));

            MD_DropSize = T2IParamTypes.Register<int>(new(
                "MD Drop Size",
                "Ignore masks smaller than this size (pixels) for detailing.",
                10, Min: 1, Max: 16384, Step: 1, // Max needs to align with MAX_RESOLUTION logic
                Toggleable: false, Group: MaskDetailerGroup, FeatureFlag: "comfyui",
                ID: "maskdetailer_drop_size", OrderPriority: 130
            ));

            WorkflowGenerator.AddStep(g =>
            {
                if (!g.Features.Contains("comfyui"))
                {
                    return;
                }

                // Activation check for MaskDetailer
                if (!g.UserInput.TryGet(MD_DetectionModel, out string detectionModelName) || string.IsNullOrEmpty(detectionModelName) || detectionModelName == "(None)")
                {
                    return; // MaskDetailer not active
                }

                // --- Get Parameter Values ---
                // UltralyticsDetectorProvider
                string mdModel = g.UserInput.Get(MD_DetectionModel);

                // SegmDetectorCombined_v2
                float mdConfidence = g.UserInput.Get(MD_ConfidenceThreshold);
                int mdDilation = g.UserInput.Get(MD_MaskDilation);

                // MaskDetailerPipe
                float mdGuideSize = g.UserInput.Get(MD_GuideSize);
                float mdMaxSize = g.UserInput.Get(MD_MaxSize);
                float mdDenoiseStrength = g.UserInput.Get(MD_Denoise);
                float mdCropFactor = g.UserInput.Get(MD_CropFactor);
                int mdDropSize = g.UserInput.Get(MD_DropSize);

                // Handle overrides, falling back to main graph values or node defaults
                long baseSeed = g.UserInput.Get(T2IParamTypes.Seed);
                long actualMdSeed = g.UserInput.TryGet(MD_Seed, out long seedVal) ? (seedVal == -1 ? baseSeed : seedVal) : baseSeed; // If MD_Seed is -1, use main seed, else use MD_Seed value, else default to main seed.

                int actualMdSteps = g.UserInput.TryGet(MD_Steps, out int stepsVal) ? stepsVal : g.UserInput.Get(T2IParamTypes.Steps);
                float actualMdCfg = g.UserInput.TryGet(MD_CFGScale, out float cfgVal) ? cfgVal : (float)g.UserInput.Get(T2IParamTypes.CFGScale);
                
                string mainSampler = g.UserInput.Get(ComfyUIBackendExtension.SamplerParam);
                string actualMdSampler = g.UserInput.TryGet(MD_Sampler, out string samplerVal) && !string.IsNullOrEmpty(samplerVal) ? samplerVal : mainSampler;
                // MaskDetailerPipe's default sampler is "euler", so if mainSampler is also null/empty, this might need a hard default.
                if (string.IsNullOrEmpty(actualMdSampler)) actualMdSampler = "euler";


                string mainScheduler = g.UserInput.Get(ComfyUIBackendExtension.SchedulerParam);
                string actualMdScheduler = g.UserInput.TryGet(MD_Scheduler, out string schedulerVal) && !string.IsNullOrEmpty(schedulerVal) ? schedulerVal : mainScheduler;
                // MaskDetailerPipe's default scheduler is "normal".
                if (string.IsNullOrEmpty(actualMdScheduler)) actualMdScheduler = "normal";


                // --- Get Core Workflow Components ---
                // These are the outputs from the main graph generation before this detailing step.
                JArray modelInput = g.FinalModel; 
                JArray clipInput = g.FinalClip;
                JArray vaeInput = g.FinalVae;
                JArray positiveCond = g.FinalPrompt; // Conditioning from positive CLIPTextEncode
                JArray negativeCond = g.FinalNegativePrompt; // Conditioning from negative CLIPTextEncode
                
                JArray imageToDetail = g.FinalImageOut; // This should be the IMAGE output of VAEDecode

                // --- Create and Connect Nodes for MaskDetailer Workflow ---

                // 1. ToBasicPipe
                string basicPipeNode = g.CreateNode("ToBasicPipe", new JObject
                {
                    ["model"] = modelInput,
                    ["clip"] = clipInput,
                    ["vae"] = vaeInput,
                    ["positive"] = positiveCond,
                    ["negative"] = negativeCond
                });
                JArray basicPipeOutput = new JArray { basicPipeNode, 0 }; // Output slot 0: basic_pipe

                // 2. UltralyticsDetectorProvider
                string detectorProviderNode = g.CreateNode("UltralyticsDetectorProvider", new JObject
                {
                    ["model_name"] = mdModel 
                });
                JArray segmDetectorInputFromProvider = new JArray { detectorProviderNode, 1 }; // Output slot 1: SEGM_DETECTOR

                // 3. SegmDetectorCombined_v2
                string segmDetectorNode = g.CreateNode("SegmDetectorCombined_v2", new JObject
                {
                    ["segm_detector"] = segmDetectorInputFromProvider,
                    ["image"] = imageToDetail, 
                    ["detection_threshold"] = mdConfidence,
                    ["dilation"] = mdDilation 
                });
                JArray maskOutputFromSegmDetector = new JArray { segmDetectorNode, 0 }; // Output slot 0: MASK

                // 4. MaskDetailerPipe
                string maskDetailerNode = g.CreateNode("MaskDetailerPipe", new JObject
                {
                    // Required inputs
                    ["image"] = imageToDetail,
                    ["mask"] = maskOutputFromSegmDetector,
                    ["basic_pipe"] = basicPipeOutput,
                    ["guide_size"] = mdGuideSize,
                    ["guide_size_for"] = true, // Default: "mask bbox"
                    ["max_size"] = mdMaxSize,
                    ["mask_mode"] = true, // Default: "masked only" (True in JSON means "masked only")
                    ["seed"] = actualMdSeed,
                    ["steps"] = actualMdSteps,
                    ["cfg"] = actualMdCfg,
                    ["sampler_name"] = actualMdSampler,
                    ["scheduler"] = actualMdScheduler,
                    ["denoise"] = mdDenoiseStrength,
                    ["feather"] = 5, // Default from MaskDetailerPipe INPUT_TYPES
                    ["crop_factor"] = mdCropFactor,
                    ["drop_size"] = mdDropSize,
                    ["refiner_ratio"] = 0.2f, // Default from MaskDetailerPipe INPUT_TYPES
                    ["batch_size"] = 1, // Default
                    ["cycle"] = 1, // Default
                    // Optional inputs that have defaults in MaskDetailerPipe Python
                    ["inpaint_model"] = false, // Default
                    ["noise_mask_feather"] = 20, // Default
                    ["bbox_fill"] = false, // Default
                    ["contour_fill"] = true // Default
                });
                g.FinalImageOut = new JArray { maskDetailerNode, 0 }; // Output slot 0: image

            }, 9); // Execution order priority, adjust if needed relative to other steps
        }
    }
}
