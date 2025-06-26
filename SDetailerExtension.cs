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
using SwarmUI.WebAPI;

namespace SDetailerExtension
{
    public class SDetailerExtension : Extension
    {
        public static T2IParamGroup Group = new("SEGS Detailer", Toggles: true, Open: false, OrderPriority: -2);

        // Parameters for SEGM/BBOX detection
        public static T2IRegisteredParam<string> DetectionModel;
        public static T2IRegisteredParam<float> SegmThreshold;
        public static T2IRegisteredParam<int> SegmDilation;
        public static T2IRegisteredParam<float> SegmCropFactor;
        public static T2IRegisteredParam<int> SegmDropSize;
        public static T2IRegisteredParam<string> SegmLabels;

        // Parameters for DetailerForEach (SEGSDetailer)
        public static T2IRegisteredParam<float> GuideSize;
        public static T2IRegisteredParam<bool> GuideSizeForBBox;
        public static T2IRegisteredParam<float> MaxSize;
        public static T2IRegisteredParam<long> Seed;
        public static T2IRegisteredParam<int> Steps;
        public static T2IRegisteredParam<float> CFGScale;
        public static T2IRegisteredParam<string> Sampler;
        public static T2IRegisteredParam<string> Scheduler;
        public static T2IRegisteredParam<float> DenoisingStrength;
        public static T2IRegisteredParam<bool> NoiseMaskEnabled;
        public static T2IRegisteredParam<bool> ForceInpaintEnabled;
        public static T2IRegisteredParam<int> FeatherAmount;
        public static T2IRegisteredParam<int> NoiseMaskFeatherAmount;
        public static T2IRegisteredParam<int> DetailerCycleCount;

        // Reused prompt and model override parameters
        public static T2IRegisteredParam<string> Prompt;
        public static T2IRegisteredParam<string> NegativePrompt;
        public static T2IRegisteredParam<T2IModel> Checkpoint;
        public static T2IRegisteredParam<T2IModel> VAE;

        public override void OnPreInit()
        {
            string nodeFolder = Path.Join(FilePath, "nodes");
            ComfyUISelfStartBackend.CustomNodePaths.Add(nodeFolder);
            Logs.Init($"Adding {nodeFolder} to CustomNodePaths");
        }

        public override void OnInit()
        {
            // Map Impact Pack nodes
            ComfyUIBackendExtension.NodeToFeatureMap["UltralyticsDetectorProvider"] = "comfyui";
            ComfyUIBackendExtension.NodeToFeatureMap["SegmDetectorSEGS"] = "comfyui";
            // ADDED: Map BboxDetectorSEGS to allow for bounding-box only models
            ComfyUIBackendExtension.NodeToFeatureMap["BboxDetectorSEGS"] = "comfyui";
            ComfyUIBackendExtension.NodeToFeatureMap["DetailerForEach"] = "comfyui";

            List<string> GetUltralyticsModels(Session session)
            {
                try
                {
                    var models = new List<string>();
                    var yoloModels = ComfyUIBackendExtension.YoloModels?.ToList();
                    if (yoloModels != null && yoloModels.Count > 0)
                    {
                        // Add yolov8 models with bbox/ prefix for compatibility with UltralyticsDetectorProvider
                        models.AddRange(yoloModels.Where(m => m != "(None)" && m != null).Select(m => "bbox/" + m));
                        // Also add them with segm/ prefix
                        models.AddRange(yoloModels.Where(m => m != "(None)" && m != null).Select(m => "segm/" + m));
                        // And add them without prefix for general ultralytics compatibility
                        models.AddRange(yoloModels.Where(m => m != "(None)" && m != null));
                    }
                    if (models.Count > 0)
                    {
                        return models.Distinct().Prepend("(None)").ToList();
                    }
                }
                catch (Exception ex)
                {
                    Logs.Error($"Error getting Ultralytics models: {ex.Message}");
                }
                return ["(None)"];
            }

            DetectionModel = T2IParamTypes.Register<string>(new("YOLO Detection Model", "Select YOLO model for detection. Models from yolov8 folder, compatible with Impact Pack UltralyticsDetectorProvider.",
                "(None)",
                Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_detection_model", OrderPriority: 10,
                GetValues: GetUltralyticsModels
            ));

            // Parameters for SEGM detection (SegmDetectorSEGS)
            SegmThreshold = T2IParamTypes.Register<float>(new("Detector Threshold", "Detection threshold for the model.", "0.5",
                Min: 0.0f, Max: 1.0f, Step: 0.01f, Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_segm_threshold", OrderPriority: 15));

            SegmDilation = T2IParamTypes.Register<int>(new("Segm. Dilation", "Dilation/erosion for segmentation masks. Positive dilates, negative erodes. (Segm models only)", "10",
                Min: -512, Max: 512, Step: 1, Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_segm_dilation", OrderPriority: 16));
            
            SegmCropFactor = T2IParamTypes.Register<float>(new("Segm. Crop Factor", "Crop factor for the segmentation. (Segm models only)", "3.0",
                Min: 1.0f, Max: 100f, Step: 0.1f, Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_segm_crop_factor", OrderPriority: 17));

            SegmDropSize = T2IParamTypes.Register<int>(new("Segm. Drop Size", "Segments smaller than this pixel size will be dropped. (Segm models only)", "10",
                Min: 1, Max: 8192, Step: 1, Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_segm_drop_size", OrderPriority: 18));

            SegmLabels = T2IParamTypes.Register<string>(new("Detector Labels", "Comma-separated list of class names or IDs (e.g., 'person, car'). 'all' for all classes.", "all",
                Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_segm_labels", OrderPriority: 20));

            // Parameters for DetailerForEach
            GuideSize = T2IParamTypes.Register<float>(new("Detailer Guide Size", "Guide size for detailing.", "512",
                Min: 64, Max: 4096, Step: 8, Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_guide_size", OrderPriority: 30));

            GuideSizeForBBox = T2IParamTypes.Register<bool>(new("Detailer Guide For BBox", "If true, guide size is for bbox; otherwise, for crop region.", "true",
                Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_guide_size_for_bbox", OrderPriority: 40));

            MaxSize = T2IParamTypes.Register<float>(new("Detailer Max Size", "Max size for detailing.", "1024",
                Min: 64, Max: 4096, Step: 8, Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_max_size", OrderPriority: 50));

            DenoisingStrength = T2IParamTypes.Register<float>(new("Detailer Denoising", "Denoising strength for detailing (0 = none, 1 = full replace).", "0.5",
                Min: 0.0f, Max: 1.0f, Step: 0.01f, Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_denoising_strength", OrderPriority: 60));

            NoiseMaskEnabled = T2IParamTypes.Register<bool>(new("Detailer Noise Mask", "Enable noise mask for detailing.", "true",
                Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_noise_mask", OrderPriority: 70));

            ForceInpaintEnabled = T2IParamTypes.Register<bool>(new("Detailer Force Inpaint", "Enable force inpaint.", "true",
                Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_force_inpaint", OrderPriority: 80));
            
            FeatherAmount = T2IParamTypes.Register<int>(new("Detailer Feather", "General feathering for the detail mask.", "5",
                Min: 0, Max: 100, Step: 1, Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_feather_amount", OrderPriority: 90));

            NoiseMaskFeatherAmount = T2IParamTypes.Register<int>(new("Detailer Noise Mask Feather", "Feathering for the noise mask specifically.", "20",
                Min: 0, Max: 100, Step: 1, Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_noise_mask_feather", OrderPriority: 95));

            DetailerCycleCount = T2IParamTypes.Register<int>(new("Detailer Cycle Count", "Number of detailing cycles.", "1",
                Min: 1, Max: 10, Step: 1, Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_cycle_count", OrderPriority: 100));

            Prompt = T2IParamTypes.Register<string>(new("Detailer Prompt", "Positive prompt for detailing. Uses main prompt if empty.",
                "", Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_prompt", OrderPriority: 110, ViewType: ParamViewType.PROMPT));

            NegativePrompt = T2IParamTypes.Register<string>(new("Detailer Negative Prompt", "Negative prompt for detailing. Uses main prompt if empty.",
                "", Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_negative_prompt", OrderPriority: 120, ViewType: ParamViewType.PROMPT));

            Seed = T2IParamTypes.Register<long>(new("Detailer Seed", "Detailing seed. 0 for random for each detail, or specify a fixed seed.",
                "0", Min: 0, Max: long.MaxValue, Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_seed", OrderPriority: 130));

            Checkpoint = T2IParamTypes.Register<T2IModel>(new("Detailer Checkpoint", "Override Checkpoint for detailing.",
                null, Toggleable: true, Group: Group, FeatureFlag: "comfyui",
                Subtype: "Stable-Diffusion", ChangeWeight: 9, ID: "segsdetailer_checkpoint", OrderPriority: 140,
                GetValues: (session) => Program.T2IModelSets["Stable-Diffusion"].ListModelNamesFor(session).Where(m => m != "(None)" && m != null).Select(T2IParamTypes.CleanModelName).Distinct().ToList()));

            VAE = T2IParamTypes.Register<T2IModel>(new("Detailer VAE", "Override VAE for detailing.",
                null, Toggleable: true, Group: Group,
                FeatureFlag: "comfyui", Subtype: "VAE", ChangeWeight: 7, ID: "segsdetailer_vae", OrderPriority: 150,
                GetValues: (session) => Program.T2IModelSets["VAE"].ListModelNamesFor(session).Where(m => m != "(None)" && m != null).Select(T2IParamTypes.CleanModelName).Distinct().ToList()));

            Sampler = T2IParamTypes.Register<string>(new("Detailer Sampler", "Override Sampler for detailing.",
                null, Toggleable: true, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_sampler", OrderPriority: 160,
                GetValues: (session) => ComfyUIBackendExtension.SamplerParam?.Type?.GetValues?.Invoke(session) ?? [] ));

            Scheduler = T2IParamTypes.Register<string>(new("Detailer Scheduler", "Override Scheduler for detailing.",
                null, Toggleable: true, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_scheduler", OrderPriority: 170,
                 GetValues: (session) => ComfyUIBackendExtension.SchedulerParam?.Type?.GetValues?.Invoke(session) ?? [] ));

            Steps = T2IParamTypes.Register<int>(new("Detailer Steps", "Override Steps for detailing.", "20",
                Min: 1, Max: 150, Step: 1, Toggleable: true, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_steps", OrderPriority: 180));

            CFGScale = T2IParamTypes.Register<float>(new("Detailer CFG Scale", "Override CFG Scale for detailing.", "8.0",
                Min: 0.0f, Max: 30.0f, Step: 0.5f, Toggleable: true, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_cfg_scale", OrderPriority: 190));

            WorkflowGenerator.AddStep(g =>
            {
                if (!g.Features.Contains("comfyui"))
                {
                    return;
                }

                if (!g.UserInput.TryGet(DetectionModel, out _))
                {
                    return;
                }

                string detectionModelName = g.UserInput.Get(DetectionModel);
                if (string.IsNullOrEmpty(detectionModelName) || detectionModelName == "(None)")
                {
                    return;
                }

                JArray lastNode = g.FinalImageOut;

                // Node 1: Use Impact Pack's UltralyticsDetectorProvider to load the model
                string detectorProviderNode = g.CreateNode("UltralyticsDetectorProvider", new JObject
                {
                    ["model_name"] = detectionModelName
                });
                // This is a reference to the 'detector' output from the provider node, which is the second output (index 1)
                JArray detectorOutput = new JArray { detectorProviderNode, 1 };

                // --- START of UPDATED LOGIC ---
                // Dynamically choose the detector node based on the model name
                string detectorNode;
                if (detectionModelName.Contains("segm", StringComparison.OrdinalIgnoreCase))
                {
                    // If the model name includes "segm", use the segmentation detector node
                    var segsDetectorInputs = new JObject
                    {
                        ["segm_detector"] = detectorOutput,
                        ["image"] = lastNode,
                        ["threshold"] = g.UserInput.Get(SegmThreshold, 0.5f),
                        ["dilation"] = g.UserInput.Get(SegmDilation, 10),
                        ["crop_factor"] = g.UserInput.Get(SegmCropFactor, 3.0f),
                        ["drop_size"] = g.UserInput.Get(SegmDropSize, 10),
                        ["labels"] = g.UserInput.Get(SegmLabels, "all")
                    };
                    detectorNode = g.CreateNode("SegmDetectorSEGS", segsDetectorInputs);
                }
                else
                {
                    // Otherwise, fall back to the bounding box detector node
                    var bboxDetectorInputs = new JObject
                    {
                        ["bbox_detector"] = detectorOutput,
                        ["image"] = lastNode,
                        ["threshold"] = g.UserInput.Get(SegmThreshold, 0.5f), // Reusing the same threshold parameter
                        ["labels"] = g.UserInput.Get(SegmLabels, "all")       // Reusing the same labels parameter
                    };
                    detectorNode = g.CreateNode("BboxDetectorSEGS", bboxDetectorInputs);
                }
                
                // The output from either detector node is the 'SEGS' data needed by the detailer
                JArray detectedSegsOutput = new JArray { detectorNode, 0 };
                // --- END of UPDATED LOGIC ---


                // Prepare inputs for DetailerForEach (Node 3)
                JArray modelInput = g.FinalModel;
                JArray clipInput = g.FinalClip;
                JArray vaeInput = g.FinalVae;

                if (g.UserInput.TryGet(VAE, out T2IModel vaeModel) && vaeModel != null)
                {
                    string vaeLoaderNode = g.CreateNode("VAELoader", new JObject { ["vae_name"] = vaeModel.Name });
                    vaeInput = new JArray { vaeLoaderNode, 0 };
                }

                if (g.UserInput.TryGet(Checkpoint, out T2IModel sdModel) && sdModel != null)
                {
                    string sdLoaderNode = g.CreateNode("CheckpointLoaderSimple", new JObject { ["ckpt_name"] = sdModel.Name });
                    modelInput = new JArray { sdLoaderNode, 0 };
                    clipInput = new JArray { sdLoaderNode, 1 };
                    if (!g.UserInput.TryGet(VAE, out T2IModel currentVaeModel) || currentVaeModel == null)
                    {
                        vaeInput = new JArray { sdLoaderNode, 2 };
                    }
                }
                
                string promptText = g.UserInput.Get(Prompt, "");
                string negativePromptText = g.UserInput.Get(NegativePrompt, "");

                JArray positiveCond = string.IsNullOrWhiteSpace(promptText) ? g.FinalPrompt : g.CreateConditioning(promptText, clipInput, null, true);
                JArray negativeCond = string.IsNullOrWhiteSpace(negativePromptText) ? g.FinalNegativePrompt : g.CreateConditioning(negativePromptText, clipInput, null, false);

                int finalSteps = g.UserInput.TryGet(Steps, out int stepsVal) ? stepsVal : g.UserInput.Get(T2IParamTypes.Steps);
                float finalCfg = g.UserInput.TryGet(CFGScale, out float cfgVal) ? cfgVal : (float)g.UserInput.Get(T2IParamTypes.CFGScale);
                string defaultSampler = g.UserInput.Get(ComfyUIBackendExtension.SamplerParam, "euler");
                string defaultScheduler = g.UserInput.Get(ComfyUIBackendExtension.SchedulerParam, "normal");
                string finalSampler = g.UserInput.TryGet(Sampler, out string samplerVal) && !string.IsNullOrWhiteSpace(samplerVal) ? samplerVal : defaultSampler;
                string finalScheduler = g.UserInput.TryGet(Scheduler, out string schedulerVal) && !string.IsNullOrWhiteSpace(schedulerVal) ? schedulerVal : defaultScheduler;

                var detailerInputs = new JObject
                {
                    ["image"] = lastNode,
                    ["segs"] = detectedSegsOutput, // This now comes from our new dynamic logic
                    ["model"] = modelInput,
                    ["clip"] = clipInput,
                    ["vae"] = vaeInput,
                    ["positive"] = positiveCond,
                    ["negative"] = negativeCond,
                    ["guide_size"] = g.UserInput.Get(GuideSize, 512f),
                    ["guide_size_for"] = g.UserInput.Get(GuideSizeForBBox, true) ? "bbox" : "crop_region",
                    ["max_size"] = g.UserInput.Get(MaxSize, 1024f),
                    ["seed"] = g.UserInput.Get(Seed, 0L),
                    ["steps"] = finalSteps,
                    ["cfg"] = finalCfg,
                    ["sampler_name"] = finalSampler,
                    ["scheduler"] = finalScheduler,
                    ["denoise"] = g.UserInput.Get(DenoisingStrength, 0.5f),
                    ["feather"] = g.UserInput.Get(FeatherAmount, 5),
                    ["noise_mask"] = g.UserInput.Get(NoiseMaskEnabled, true),
                    ["force_inpaint"] = g.UserInput.Get(ForceInpaintEnabled, true),
                    ["wildcard"] = "",  // Empty wildcard - no dynamic prompts
                    ["cycle"] = g.UserInput.Get(DetailerCycleCount, 1),
                    ["noise_mask_feather"] = g.UserInput.Get(NoiseMaskFeatherAmount, 20)
                };
                string detailerNode = g.CreateNode("DetailerForEach", detailerInputs);

                g.FinalImageOut = new JArray { detailerNode, 0 };

            }, 9);
        }
    }
}
