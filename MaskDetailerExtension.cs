using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.Text2Image;
using SwarmUI.Builtin_ComfyUIBackend;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO; 
using System;
using System.Linq;
using SwarmUI.Accounts; 
using FreneticUtilities.FreneticExtensions; 
using SwarmUI.WebAPI; // Added back

namespace SDetailerExtension // Namespace can remain or be changed if you prefer
{
    public class MaskDetailerExtension : Extension
    {
        // New group for MaskDetailer parameters
        public static T2IParamGroup MaskDetailerGroup = new("MaskDetailer", "MaskDetailer Workflow Options", Toggles: true, Open: false, OrderPriority: -1);

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

        // Added back an empty OnPreInit, in case the lifecycle method call itself is important for extension recognition
        public override void OnPreInit()
        {
            // Base OnPreInit might be called by SwarmUI core.
            // We don't have custom Python nodes to register paths for with this version,
            // as Impact Pack nodes are assumed to be globally available to ComfyUI.
            // If other pre-initialization logic specific to this extension were needed, it would go here.
            Logs.Init($"MaskDetailerExtension OnPreInit (currently does nothing specific)."); // Optional: for logging
        }

        public override void OnInit()
        {
            // The new nodes (ToBasicPipe, etc.) are assumed to be part of a known pack (Impact Pack)
            // and generally do not need explicit NodeToFeatureMap entries if they don't require special SwarmUI feature flags
            // beyond the main "comfyui" backend flag.

            // --- Parameters for UltralyticsDetectorProvider ---
            MD_DetectionModel = T2IParamTypes.Register<string>(new(
                "MD Detection Model",
                "Select detection model for MaskDetailer (e.g., segmentation models like 'segm/model.pt'). Models should be in the appropriate ComfyUI custom node model paths (eg. ComfyUI/models/ultralytics/segm).",
                "(None)", 
                Toggleable: false, 
                Group: MaskDetailerGroup,
                FeatureFlag: "comfyui",
                ID: "maskdetailer_detection_model",
                OrderPriority: 10,
                GetValues: (session) => {
                    var exampleModels = new List<string> { "(None)", "segm/99coins_anime_girl_face_m_seg.pt", "segm/bbox/face_yolov8m.pt" };
                    // TODO: Implement robust fetching of models from ComfyUI/ultralytics folders or via API
                    return exampleModels;
                }
            ));

            // --- Parameters for SegmDetectorCombined_v2 ---
            MD_ConfidenceThreshold = T2IParamTypes.Register<float>(new(
                "MD Confidence Threshold",
                "Minimum detection score (0-1) for SegmDetector. Lower = more detections.",
                0.3f, 
                Min: 0.0f, Max: 1.0f, Step: 0.01f, 
                Toggleable: false,
                Group: MaskDetailerGroup,
                FeatureFlag: "comfyui",
                ID: "maskdetailer_confidence_threshold",
                OrderPriority: 20
            ));

            MD_MaskDilation = T2IParamTypes.Register<int>(new(
                "MD Mask Dilation",
                "Dilation pixels for SegmDetector. Positive values expand the mask. 0 for no change.",
                0, 
                Min: 0, Max: 128, Step: 1, 
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
                512f, Min: 64f, Max: 16384f, Step: 64f, 
                Toggleable: false, Group: MaskDetailerGroup, FeatureFlag: "comfyui",
                ID: "maskdetailer_guide_size", OrderPriority: 40
            ));

            MD_MaxSize = T2IParamTypes.Register<float>(new(
                "MD Max Size",
                "Maximum size constraint for the detailing process (pixels).",
                1024f, Min: 64f, Max: 16384f, Step: 64f, 
                Toggleable: false, Group: MaskDetailerGroup, FeatureFlag: "comfyui",
                ID: "maskdetailer_max_size", OrderPriority: 50
            ));

            MD_Steps = T2IParamTypes.Register<int>(new(
                "MD Steps (Override)",
                "Override sampling steps for MaskDetailer. Uses main steps if not enabled.",
                20, Min: 1, Max: 10000, 
                Toggleable: true, Group: MaskDetailerGroup, FeatureFlag: "comfyui",
                ID: "maskdetailer_steps", OrderPriority: 60
            ));

            MD_CFGScale = T2IParamTypes.Register<float>(new(
                "MD CFG Scale (Override)",
                "Override CFG Scale for MaskDetailer. Uses main CFG if not enabled.",
                8.0f, Min: 0.0f, Max: 100.0f, Step: 0.1f, 
                Toggleable: true, Group: MaskDetailerGroup, FeatureFlag: "comfyui",
                ID: "maskdetailer_cfg_scale", OrderPriority: 70
            ));

            MD_Sampler = T2IParamTypes.Register<string>(new(
                "MD Sampler (Override)",
                "Override sampler for MaskDetailer. Uses main sampler if not enabled.",
                null, 
                Toggleable: true, Group: MaskDetailerGroup, FeatureFlag: "comfyui",
                ID: "maskdetailer_sampler", OrderPriority: 80,
                GetValues: (session) => {
                    T2IParamType samplerType = ComfyUIBackendExtension.SamplerParam?.Type;
                    if (samplerType?.GetValues != null) { try { return samplerType.GetValues(session); } catch { } }
                    return new List<string>(ComfyUIBackendExtension.SamplersDefault); 
                }
            ));

            MD_Scheduler = T2IParamTypes.Register<string>(new(
                "MD Scheduler (Override)",
                "Override scheduler for MaskDetailer. Uses main scheduler if not enabled.",
                null, 
                Toggleable: true, Group: MaskDetailerGroup, FeatureFlag: "comfyui",
                ID: "maskdetailer_scheduler", OrderPriority: 90,
                GetValues: (session) => {
                    T2IParamType schedulerType = ComfyUIBackendExtension.SchedulerParam?.Type;
                    if (schedulerType?.GetValues != null) { try { return schedulerType.GetValues(session); } catch { } }
                    return new List<string>(ComfyUIBackendExtension.SchedulersDefault); 
                }
            ));
            
            MD_Seed = T2IParamTypes.Register<long>(new(
                "MD Seed (Override)",
                "MaskDetailer seed. Uses main image seed if not enabled (-1). Set to 0 for MaskDetailer's internal random.",
                0, Min: -1, Max: long.MaxValue, 
                Toggleable: true, Group: MaskDetailerGroup, FeatureFlag: "comfyui",
                ID: "maskdetailer_seed", OrderPriority: 100
            ));

            MD_Denoise = T2IParamTypes.Register<float>(new(
                "MD Denoise Strength",
                "Denoising strength for the MaskDetailer process (0 = none, 1 = full replace).",
                0.5f, Min: 0.0f, Max: 1.0f, Step: 0.01f, 
                Toggleable: false, Group: MaskDetailerGroup, FeatureFlag: "comfyui",
                ID: "maskdetailer_denoise", OrderPriority: 110
            ));

            MD_CropFactor = T2IParamTypes.Register<float>(new(
                "MD Crop Factor",
                "Factor to expand the bounding box for cropping before detailing (1.0 = no expansion).",
                3.0f, Min: 1.0f, Max: 10f, Step: 0.1f, 
                Toggleable: false, Group: MaskDetailerGroup, FeatureFlag: "comfyui",
                ID: "maskdetailer_crop_factor", OrderPriority: 120
            ));

            MD_DropSize = T2IParamTypes.Register<int>(new(
                "MD Drop Size",
                "Ignore masks smaller than this size (pixels) for detailing.",
                10, Min: 1, Max: 16384, Step: 1, 
                Toggleable: false, Group: MaskDetailerGroup, FeatureFlag: "comfyui",
                ID: "maskdetailer_drop_size", OrderPriority: 130
            ));

            WorkflowGenerator.AddStep(g =>
            {
                if (!g.Features.Contains("comfyui"))
                {
                    return;
                }

                if (!g.UserInput.TryGet(MD_DetectionModel, out string detectionModelName) || string.IsNullOrEmpty(detectionModelName) || detectionModelName == "(None)")
                {
                    return; 
                }

                string mdModel = g.UserInput.Get(MD_DetectionModel);
                float mdConfidence = g.UserInput.Get(MD_ConfidenceThreshold);
                int mdDilation = g.UserInput.Get(MD_MaskDilation);
                float mdGuideSize = g.UserInput.Get(MD_GuideSize);
                float mdMaxSize = g.UserInput.Get(MD_MaxSize);
                float mdDenoiseStrength = g.UserInput.Get(MD_Denoise);
                float mdCropFactor = g.UserInput.Get(MD_CropFactor);
                int mdDropSize = g.UserInput.Get(MD_DropSize);

                long baseSeed = g.UserInput.Get(T2IParamTypes.Seed);
                long actualMdSeed = g.UserInput.TryGet(MD_Seed, out long seedVal) ? (seedVal == -1 ? baseSeed : seedVal) : baseSeed;
                int actualMdSteps = g.UserInput.TryGet(MD_Steps, out int stepsVal) ? stepsVal : g.UserInput.Get(T2IParamTypes.Steps);
                float actualMdCfg = g.UserInput.TryGet(MD_CFGScale, out float cfgVal) ? cfgVal : (float)g.UserInput.Get(T2IParamTypes.CFGScale);
                
                string mainSampler = g.UserInput.Get(ComfyUIBackendExtension.SamplerParam);
                string actualMdSampler = g.UserInput.TryGet(MD_Sampler, out string samplerVal) && !string.IsNullOrEmpty(samplerVal) ? samplerVal : mainSampler;
                if (string.IsNullOrEmpty(actualMdSampler)) actualMdSampler = "euler";

                string mainScheduler = g.UserInput.Get(ComfyUIBackendExtension.SchedulerParam);
                string actualMdScheduler = g.UserInput.TryGet(MD_Scheduler, out string schedulerVal) && !string.IsNullOrEmpty(schedulerVal) ? schedulerVal : mainScheduler;
                if (string.IsNullOrEmpty(actualMdScheduler)) actualMdScheduler = "normal";

                JArray modelInput = g.FinalModel; 
                JArray clipInput = g.FinalClip;
                JArray vaeInput = g.FinalVae;
                JArray positiveCond = g.FinalPrompt; 
                JArray negativeCond = g.FinalNegativePrompt; 
                JArray imageToDetail = g.FinalImageOut; 

                string basicPipeNode = g.CreateNode("ToBasicPipe", new JObject
                {
                    ["model"] = modelInput,
                    ["clip"] = clipInput,
                    ["vae"] = vaeInput,
                    ["positive"] = positiveCond,
                    ["negative"] = negativeCond
                });
                JArray basicPipeOutput = new JArray { basicPipeNode, 0 }; 

                string detectorProviderNode = g.CreateNode("UltralyticsDetectorProvider", new JObject
                {
                    ["model_name"] = mdModel 
                });
                JArray segmDetectorInputFromProvider = new JArray { detectorProviderNode, 1 }; 

                string segmDetectorNode = g.CreateNode("SegmDetectorCombined_v2", new JObject
                {
                    ["segm_detector"] = segmDetectorInputFromProvider,
                    ["image"] = imageToDetail, 
                    ["detection_threshold"] = mdConfidence,
                    ["dilation"] = mdDilation 
                });
                JArray maskOutputFromSegmDetector = new JArray { segmDetectorNode, 0 };

                string maskDetailerNode = g.CreateNode("MaskDetailerPipe", new JObject
                {
                    ["image"] = imageToDetail,
                    ["mask"] = maskOutputFromSegmDetector,
                    ["basic_pipe"] = basicPipeOutput,
                    ["guide_size"] = mdGuideSize,
                    ["guide_size_for"] = true, 
                    ["max_size"] = mdMaxSize,
                    ["mask_mode"] = true, 
                    ["seed"] = actualMdSeed,
                    ["steps"] = actualMdSteps,
                    ["cfg"] = actualMdCfg,
                    ["sampler_name"] = actualMdSampler,
                    ["scheduler"] = actualMdScheduler,
                    ["denoise"] = mdDenoiseStrength,
                    ["feather"] = 5, 
                    ["crop_factor"] = mdCropFactor,
                    ["drop_size"] = mdDropSize,
                    ["refiner_ratio"] = 0.2f, 
                    ["batch_size"] = 1, 
                    ["cycle"] = 1, 
                    ["inpaint_model"] = false, 
                    ["noise_mask_feather"] = 20, 
                    ["bbox_fill"] = false, 
                    ["contour_fill"] = true 
                });
                g.FinalImageOut = new JArray { maskDetailerNode, 0 }; 

            }, 9); 
        }
    }
}
