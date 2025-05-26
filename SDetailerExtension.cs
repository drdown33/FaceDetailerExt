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
        public static T2IParamGroup Group = new("SEGS Detailer", Toggles: true, Open: false, OrderPriority: -2); // Changed group name

        // Parameters for SEGM detection
        public static T2IRegisteredParam<string> DetectionModel;
        public static T2IRegisteredParam<float> ConfidenceThreshold; // Reused for SegmDetectorSEGS
        public static T2IRegisteredParam<string> ClassFilter; // Potentially for SegmDetectorSEGS if it supports similar filtering

        // Parameters for DetailerForEach (SEGSDetailer)
        public static T2IRegisteredParam<float> GuideSize;
        public static T2IRegisteredParam<bool> GuideSizeForBBox;
        public static T2IRegisteredParam<float> MaxSize;
        public static T2IRegisteredParam<long> Seed; // Reused
        public static T2IRegisteredParam<int> Steps; // Reused
        public static T2IRegisteredParam<float> CFGScale; // Reused
        public static T2IRegisteredParam<string> Sampler; // Reused
        public static T2IRegisteredParam<string> Scheduler; // Reused
        public static T2IRegisteredParam<float> DenoisingStrength; // Reused (maps to 'denoise')
        public static T2IRegisteredParam<bool> NoiseMaskEnabled;
        public static T2IRegisteredParam<bool> ForceInpaintEnabled;
        public static T2IRegisteredParam<int> FeatherAmount; // New, replaces MaskBlur for general feather
        public static T2IRegisteredParam<int> NoiseMaskFeatherAmount; // New, for noise_mask_feather
        public static T2IRegisteredParam<int> DetailerCycleCount;

        // Reused prompt and model override parameters
        public static T2IRegisteredParam<string> Prompt;
        public static T2IRegisteredParam<string> NegativePrompt;
        public static T2IRegisteredParam<T2IModel> Checkpoint;
        public static T2IRegisteredParam<T2IModel> VAE;

        // Example additional params for SegmDetectorSEGS, if needed
        // public static T2IRegisteredParam<int> SegmErosion;
        // public static T2IRegisteredParam<int> SegmDropSmallAreaPx;


        public override void OnPreInit()
        {
            // If your custom nodes for SEGSDetailer are in a subfolder of the extension, add it here.
            // string nodeFolder = Path.Join(FilePath, "nodes"); // Assuming Impact Pack nodes are globally available or managed by ComfyUISelfStartBackend
            // ComfyUISelfStartBackend.CustomNodePaths.Add(nodeFolder);
            // Logs.Init($"Adding {nodeFolder} to CustomNodePaths");
        }

        public override void OnInit()
        {
            // Update NodeToFeatureMap for the new nodes
            ComfyUIBackendExtension.NodeToFeatureMap["UltralyticsDetectorProvider"] = "comfyui";
            ComfyUIBackendExtension.NodeToFeatureMap["SegmDetectorSEGS"] = "comfyui";
            ComfyUIBackendExtension.NodeToFeatureMap["DetailerForEach"] = "comfyui"; // Main detailing node from SEGS Detailer.json

            List<string> GetSegmModels(Session session)
            {
                try
                {
                    // This assumes ComfyUI can list models from 'ultralytics/segm' and they are registered in a way SwarmUI can access.
                    // Or, that ComfyUIBackendExtension.YoloModels is populated with "segm/model.pt" style names.
                    // For a robust solution, this might need to scan the ComfyUI models/ultralytics/segm directory.
                    var comfySegmPath = Path.Combine(ComfyUIBackendExtension.ComfyPathSafe, "models", "ultralytics", "segm");
                    if (Directory.Exists(comfySegmPath))
                    {
                        return Directory.EnumerateFiles(comfySegmPath, "*.pt", SearchOption.AllDirectories)
                            .Concat(Directory.EnumerateFiles(comfySegmPath, "*.safetensors", SearchOption.AllDirectories))
                            .Select(f => "segm/" + Path.GetRelativePath(comfySegmPath, f).Replace(Path.DirectorySeparatorChar, '/'))
                            .Prepend("(None)").ToList();
                    }
                    // Fallback to YoloModels if specifically populated for this
                    var yoloModels = ComfyUIBackendExtension.YoloModels?.ToList();
                     if (yoloModels != null && yoloModels.Any(m => m.StartsWith("segm/")))
                    {
                        return yoloModels.Prepend("(None)").ToList();
                    }
                }
                catch (Exception ex)
                {
                    Logs.Error($"Error getting SEGM models: {ex.Message}");
                }
                return ["(None)"];
            }

            DetectionModel = T2IParamTypes.Register<string>(new("SEGM Detection Model", "Select SEGM model (e.g., from 'ultralytics/segm' folder). Ensure model names are prefixed with 'segm/'.",
                "(None)",
                Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_detection_model", OrderPriority: 10,
                GetValues: GetSegmModels
            ));

            ConfidenceThreshold = T2IParamTypes.Register<float>(new("Detection Threshold", "Minimum detection score for SegmDetectorSEGS.", "0.5", // Default from SEGS Detailer.json for SegmDetectorSEGS
                Min: 0.01f, Max: 1.0f, Step: 0.01f, Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_confidence_threshold", OrderPriority: 20));

            ClassFilter = T2IParamTypes.Register<string>(new("Class Filter", "Comma-separated list of class names or IDs for SegmDetectorSEGS (e.g., 'person, car'). Leave empty or use 'all' for all classes.", "all",
                Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_class_filter", OrderPriority: 25));

            // Parameters for DetailerForEach
            GuideSize = T2IParamTypes.Register<float>(new("Guide Size", "Guide size for detailing.", "512",
                Min: 64, Max: 4096, Step: 8, Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_guide_size", OrderPriority: 30));

            GuideSizeForBBox = T2IParamTypes.Register<bool>(new("Guide Size For BBox", "If true, guide size is for bbox; otherwise, for crop region.", "true",
                Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_guide_size_for_bbox", OrderPriority: 40));

            MaxSize = T2IParamTypes.Register<float>(new("Max Size", "Max size for detailing.", "1024", // From SEGS Detailer.json example for DetailerForEach
                Min: 64, Max: 4096, Step: 8, Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_max_size", OrderPriority: 50));

            DenoisingStrength = T2IParamTypes.Register<float>(new("Denoising Strength", "Denoising strength for detailing (0 = none, 1 = full replace).", "0.5", // Default from SEGSDetailer class / DetailerForEach JSON
                Min: 0.0f, Max: 1.0f, Step: 0.01f, Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_denoising_strength", OrderPriority: 60));

            NoiseMaskEnabled = T2IParamTypes.Register<bool>(new("Noise Mask", "Enable noise mask for detailing.", "true",
                Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_noise_mask", OrderPriority: 70));

            ForceInpaintEnabled = T2IParamTypes.Register<bool>(new("Force Inpaint", "Enable force inpaint.", "true",
                Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_force_inpaint", OrderPriority: 80));

            FeatherAmount = T2IParamTypes.Register<int>(new("Feather Amount", "General feathering for the detail mask.", "5", // Default from DetailerForEach JSON
                Min: 0, Max: 100, Step: 1, Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_feather_amount", OrderPriority: 90));
            
            NoiseMaskFeatherAmount = T2IParamTypes.Register<int>(new("Noise Mask Feather", "Feathering for the noise mask specifically.", "20", // Default from SEGSDetailer class / DetailerForEach JSON
                Min: 0, Max: 100, Step: 1, Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_noise_mask_feather", OrderPriority: 95));

            DetailerCycleCount = T2IParamTypes.Register<int>(new("Cycle Count", "Number of detailing cycles.", "1",
                Min: 1, Max: 10, Step: 1, Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_cycle_count", OrderPriority: 100));

            Prompt = T2IParamTypes.Register<string>(new("Detailer Prompt", "Positive prompt for detailing. Uses main prompt if empty.",
                "", Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_prompt", OrderPriority: 110, ViewType: ParamViewType.PROMPT));

            NegativePrompt = T2IParamTypes.Register<string>(new("Detailer Negative Prompt", "Negative prompt for detailing. Uses main prompt if empty.",
                "", Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_negative_prompt", OrderPriority: 120, ViewType: ParamViewType.PROMPT));

            Seed = T2IParamTypes.Register<long>(new("Detailer Seed", "Detailing seed. 0 for random for each detail, or specify a fixed seed. (Original SDetailer used -1 for main image seed).",
                "0", Min: 0, Max: long.MaxValue, Toggleable: false, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_seed", OrderPriority: 130)); // Default 0 for SEGSDetailer

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

            Steps = T2IParamTypes.Register<int>(new("Detailer Steps", "Override Steps for detailing.", "20", // Default 20 for SEGSDetailer
                Min: 1, Max: 150, Step: 1, Toggleable: true, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_steps", OrderPriority: 180));

            CFGScale = T2IParamTypes.Register<float>(new("Detailer CFG Scale", "Override CFG Scale for detailing.", "8.0", // Default 8.0 for SEGSDetailer
                Min: 0.0f, Max: 30.0f, Step: 0.5f, Toggleable: true, Group: Group, FeatureFlag: "comfyui", ID: "segsdetailer_cfg_scale", OrderPriority: 190));

            WorkflowGenerator.AddStep(g =>
            {
                if (!g.Features.Contains("comfyui") || !g.UserInput.Get(Group.Toggled, false))
                {
                    return;
                }

                string detectionModelName = g.UserInput.Get(DetectionModel);
                if (string.IsNullOrEmpty(detectionModelName) || detectionModelName == "(None)")
                {
                    return;
                }

                JArray lastNode = g.FinalImageOut; // Image from VAE Decode or previous step

                // Node 1: UltralyticsDetectorProvider
                string detectorProviderNode = g.CreateNode("UltralyticsDetectorProvider", new JObject
                {
                    ["model_name"] = detectionModelName
                });
                JArray segmDetectorOutput = new JArray { detectorProviderNode, 1 }; // SEGM_DETECTOR is output 1

                // Node 2: SegmDetectorSEGS
                var segsDetectorInputs = new JObject
                {
                    ["segm_detector"] = segmDetectorOutput,
                    ["image"] = lastNode,
                    ["detection_threshold"] = g.UserInput.Get(ConfidenceThreshold, 0.5f),
                    // ["seg_erosion"] = g.UserInput.Get(SegmErosion, 10), // Example if param is added
                    // ["drop_small_area_px"] = g.UserInput.Get(SegmDropSmallAreaPx, 10), // Example
                    ["filter_classes"] = g.UserInput.Get(ClassFilter, "all") // Assuming SegmDetectorSEGS uses this input key
                };
                string segsNode = g.CreateNode("SegmDetectorSEGS", segsDetectorInputs);
                JArray detectedSegsOutput = new JArray { segsNode, 0 }; // SEGS is output 0

                // Prepare inputs for DetailerForEach
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
                    // If the checkpoint loader also provides a VAE (output 2), and no specific VAE override, use it.
                     if ((!g.UserInput.TryGet(VAE, out T2IModel currentVaeModel) || currentVaeModel == null) && g.NodeTypes["CheckpointLoaderSimple"].Outputs.Count > 2 && g.NodeTypes["CheckpointLoaderSimple"].Outputs[2] == "VAE")
                    {
                        vaeInput = new JArray { sdLoaderNode, 2 };
                    }
                }
                
                string promptText = g.UserInput.Get(Prompt, "");
                string negativePromptText = g.UserInput.Get(NegativePrompt, "");

                JArray positiveCond = string.IsNullOrWhiteSpace(promptText) ? g.FinalPrompt : g.CreateConditioning(promptText, clipInput, modelInput, true); // Use detailer's model/clip if overridden
                JArray negativeCond = string.IsNullOrWhiteSpace(negativePromptText) ? g.FinalNegativePrompt : g.CreateConditioning(negativePromptText, clipInput, modelInput, false);


                int finalSteps = g.UserInput.TryGet(Steps, out int stepsVal) ? stepsVal : g.UserInput.Get(T2IParamTypes.Steps);
                float finalCfg = g.UserInput.TryGet(CFGScale, out float cfgVal) ? cfgVal : (float)g.UserInput.Get(T2IParamTypes.CFGScale);
                string defaultSampler = g.UserInput.Get(ComfyUIBackendExtension.SamplerParam, "euler");
                string defaultScheduler = g.UserInput.Get(ComfyUIBackendExtension.SchedulerParam, "normal");
                string finalSampler = g.UserInput.TryGet(Sampler, out string samplerVal) && !string.IsNullOrWhiteSpace(samplerVal) ? samplerVal : defaultSampler;
                string finalScheduler = g.UserInput.TryGet(Scheduler, out string schedulerVal) && !string.IsNullOrWhiteSpace(schedulerVal) ? schedulerVal : defaultScheduler;

                // Node 3: DetailerForEach
                var detailerInputs = new JObject
                {
                    ["image"] = lastNode, // Original image to detail
                    ["segs"] = detectedSegsOutput,
                    ["model"] = modelInput,
                    ["clip"] = clipInput,
                    ["vae"] = vaeInput,
                    ["positive"] = positiveCond,
                    ["negative"] = negativeCond,
                    ["guide_size"] = g.UserInput.Get(GuideSize, 512f),
                    ["guide_size_for_bbox"] = g.UserInput.Get(GuideSizeForBBox, true),
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
                    ["cycle"] = g.UserInput.Get(DetailerCycleCount, 1), // Matches DetailerForEach input name 'cycle'
                    ["noise_mask_feather"] = g.UserInput.Get(NoiseMaskFeatherAmount, 20)
                    // Optional inputs from SEGSDetailer/DetailerForEach like 'bbox_round_value', 'denoise_model(inpaint)', 'use_refiner' can be added here if T2IParams are created
                };
                string detailerNode = g.CreateNode("DetailerForEach", detailerInputs);

                g.FinalImageOut = new JArray { detailerNode, 0 }; // IMAGE is output 0

            }, 9); // Runs with priority 9, can be adjusted
        }
    }
}
