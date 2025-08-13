using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Legacy;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.SocialPlatforms;
using UnityEngine.UIElements;
using static Unity.Rendering.Universal.ShaderUtils;
using static UnityEditor.Rendering.Universal.ShaderGraph.SubShaderUtils;

namespace UnityEditor.Rendering.Universal.ShaderGraph
{
    sealed class UniversalUnlitShurikenSubTarget : UniversalSubTarget, ILegacyTarget
    {
        static readonly GUID kSourceCodeGuid = new GUID("97c3f7dcb477ec842aa878573640310f"); // UniversalUnlitSubTarget.cs

        public override int latestVersion => 2;

        public UniversalUnlitShurikenSubTarget()
        {
            displayName = "Shuriken Unlit";
        }

        public static PragmaCollection ProceduralPragma() => new PragmaCollection
        {
            { new PragmaDescriptor { value = $"instancing_options procedural:{particleInstancingFuncName}" } }
        };

        private const string particleInstancingIncludePath = "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ParticlesInstancing.hlsl";
        private const string particleInstancingFuncName = "ParticleInstancingSetup";

        [SerializeField] private bool customVertexStream;

        [Flags]
        enum CustomVertexStream
        {
            None,
            Color = 1 << 0,
            AnimFrame = 1 << 1,
            AnimBlend = 1 << 2,
            Age = 1 << 3,
            Speed = 1 << 4,
        }

        static StructCollection ParticleStruct(CustomVertexStream customVertexStream)
        {
            StructCollection particleStruct = new StructCollection();

            //List<FieldDescriptor> fields = new List<FieldDescriptor>() { new FieldDescriptor("CustomParticleInstanceData", "transform", "", "float3x4") };
            //if ((customVertexStream & CustomVertexStream.Color) != 0)
            //    fields.Add(new FieldDescriptor("CustomParticleInstanceData", "color", "", ShaderValueType.Uint));
            //if ((customVertexStream & CustomVertexStream.AnimFrame) != 0)
            //    fields.Add(new FieldDescriptor("CustomParticleInstanceData", "animFrame", "", ShaderValueType.Float));
            //if ((customVertexStream & CustomVertexStream.AnimBlend) != 0)
            //    fields.Add(new FieldDescriptor("CustomParticleInstanceData", "animBlend", "", ShaderValueType.Float));
            //if ((customVertexStream & CustomVertexStream.Age) != 0)
            //    fields.Add(new FieldDescriptor("CustomParticleInstanceData", "agePercent", "", ShaderValueType.Float));

            //StructDescriptor particleStructDescriptor = new StructDescriptor()
            //{
            //    name = "CustomParticleInstanceData",
            //    packFields = false,
            //    populateWithCustomInterpolators = false,
            //    fields = fields.ToArray()
            //};

            //particleStruct.Add(particleStructDescriptor);

            return particleStruct;
        }

        static string ParticleDataHLSL(CustomVertexStream customVertexStream)
        {
            string hlsl = "struct CustomParticleInstanceData\n{\n    float3x4 transform;\n";
            if ((customVertexStream & CustomVertexStream.Color) != 0)
                hlsl += "    uint color;\n";
            if ((customVertexStream & CustomVertexStream.AnimFrame) != 0)
                hlsl += "    float animFrame;\n";
            if ((customVertexStream & CustomVertexStream.AnimBlend) != 0)
                hlsl += "    float animBlend;\n";
            if ((customVertexStream & CustomVertexStream.Age) != 0)
                hlsl += "    float agePercent;\n";
            if ((customVertexStream & CustomVertexStream.Speed) != 0)
                hlsl += "    float speed;\n";
            hlsl += "};\n";
            return hlsl;
        }

        static KeywordDescriptor CustomParticleDataDefine(CustomVertexStream customVertexStream)
        {
            KeywordDescriptor customParticleDataDescriptor = new KeywordDescriptor()
            {
                displayName = "ParticleInstanceData",
                //            referenceName = $@"CUSTOM_PARTICLE_DATA
                //#ifndef UNITY_PARTICLE_INSTANCE_DATA
                //#define UNITY_PARTICLE_INSTANCE_DATA CustomParticleInstanceData
                //{ParticleDataHLSL()}
                //#endif//",
                referenceName = $"UNITY_PARTICLE_INSTANCE_DATA CustomParticleInstanceData\n{ParticleDataHLSL(customVertexStream)}//",
                type = KeywordType.Boolean,
                definition = KeywordDefinition.Predefined,
            };
            return customParticleDataDescriptor;
        }

        [SerializeField] private CustomVertexStream vertexStream = CustomVertexStream.Color;

        protected override ShaderID shaderID => ShaderID.SG_Unlit;

        public override bool IsActive() => true;

        public override void Setup(ref TargetSetupContext context)
        {
            context.AddAssetDependency(kSourceCodeGuid, AssetCollection.Flags.SourceDependency);
            base.Setup(ref context);

            var universalRPType = typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset);
            if (!context.HasCustomEditorForRenderPipeline(universalRPType))
            {
                //var gui = typeof(ShaderGraphUnlitGUI);
                var gui = typeof(ShurikenUnlitGUI);
#if HAS_VFX_GRAPH
                if (TargetsVFX())
                    gui = typeof(VFXShaderGraphUnlitGUI);
#endif
                context.AddCustomEditorForRenderPipeline(gui.FullName, universalRPType);
            }
            // Process SubShaders
            context.AddSubShader(PostProcessSubShader(SubShaders.Unlit(target, target.renderType, target.renderQueue, target.disableBatching, customVertexStream, vertexStream)));
        }

        public override void ProcessPreviewMaterial(Material material)
        {
            if (target.allowMaterialOverride)
            {
                // copy our target's default settings into the material
                // (technically not necessary since we are always recreating the material from the shader each time,
                // which will pull over the defaults from the shader definition)
                // but if that ever changes, this will ensure the defaults are set
                material.SetFloat(Property.SurfaceType, (float)target.surfaceType);
                material.SetFloat(Property.BlendMode, (float)target.alphaMode);
                material.SetFloat(Property.AlphaClip, target.alphaClip ? 1.0f : 0.0f);
                material.SetFloat(Property.CullMode, (int)target.renderFace);
                material.SetFloat(Property.CastShadows, target.castShadows ? 1.0f : 0.0f);
                material.SetFloat(Property.ZWriteControl, (float)target.zWriteControl);
                material.SetFloat(Property.ZTest, (float)target.zTestMode);
            }

            // We always need these properties regardless of whether the material is allowed to override
            // Queue control & offset enable correct automatic render queue behavior
            // Control == 0 is automatic, 1 is user-specified render queue
            material.SetFloat(Property.QueueOffset, 0.0f);
            material.SetFloat(Property.QueueControl, (float)BaseShaderGUI.QueueControl.Auto);

            if (IsSpacewarpSupported())
                material.SetFloat(Property.XrMotionVectorsPass, 1.0f);

            // call the full unlit material setup function
            ShaderGraphUnlitGUI.UpdateMaterial(material, MaterialUpdateType.CreatedNewMaterial);
        }

        public override void GetFields(ref TargetFieldContext context)
        {
            base.GetFields(ref context);
        }

        public override void GetActiveBlocks(ref TargetActiveBlockContext context)
        {
            context.AddBlock(UniversalBlockFields.VertexDescription.MotionVector, target.additionalMotionVectorMode == AdditionalMotionVectorMode.Custom);

            context.AddBlock(BlockFields.SurfaceDescription.Alpha, (target.surfaceType == SurfaceType.Transparent || target.alphaClip) || target.allowMaterialOverride);
            context.AddBlock(BlockFields.SurfaceDescription.AlphaClipThreshold, target.alphaClip || target.allowMaterialOverride);
        }

        public override void CollectShaderProperties(PropertyCollector collector, GenerationMode generationMode)
        {
            if (target.allowMaterialOverride)
            {
                collector.AddFloatProperty(Property.CastShadows, target.castShadows ? 1.0f : 0.0f);
                collector.AddFloatProperty(Property.SurfaceType, (float)target.surfaceType);
                collector.AddFloatProperty(Property.BlendMode, (float)target.alphaMode);
                collector.AddFloatProperty(Property.AlphaClip, target.alphaClip ? 1.0f : 0.0f);
                collector.AddFloatProperty(Property.SrcBlend, 1.0f);    // always set by material inspector
                collector.AddFloatProperty(Property.DstBlend, 0.0f);    // always set by material inspector
                collector.AddToggleProperty(Property.ZWrite, (target.surfaceType == SurfaceType.Opaque));
                collector.AddFloatProperty(Property.ZWriteControl, (float)target.zWriteControl);
                collector.AddFloatProperty(Property.ZTest, (float)target.zTestMode);    // ztest mode is designed to directly pass as ztest
                collector.AddFloatProperty(Property.CullMode, (float)target.renderFace);    // render face enum is designed to directly pass as a cull mode

                bool enableAlphaToMask = (target.alphaClip && (target.surfaceType == SurfaceType.Opaque));
                collector.AddFloatProperty(Property.AlphaToMask, enableAlphaToMask ? 1.0f : 0.0f);
            }

            // We always need these properties regardless of whether the material is allowed to override other shader properties.
            // Queue control & offset enable correct automatic render queue behavior.  Control == 0 is automatic, 1 is user-specified.
            // We initialize queue control to -1 to indicate to UpdateMaterial that it needs to initialize it properly on the material.
            collector.AddFloatProperty(Property.QueueOffset, 0.0f);
            collector.AddFloatProperty(Property.QueueControl, -1.0f);

            if (IsSpacewarpSupported())
                collector.AddFloatProperty(Property.XrMotionVectorsPass, 1.0f);
        }

        public override void GetPropertiesGUI(ref TargetPropertyGUIContext context, Action onChange, Action<String> registerUndo)
        {
            var universalTarget = (target as UniversalTarget);

            // TODO: add properties

            context.AddProperty("Custom Vertex Stream", new Toggle() { value = customVertexStream }, (evt) =>
            {
                if (Equals(customVertexStream, evt.newValue))
                    return;

                registerUndo("Change Custom Vertex Stream");
                customVertexStream = evt.newValue;
                onChange();
            });

            context.AddProperty("Vertex Stream", new EnumFlagsField(CustomVertexStream.None) { value = vertexStream , enabledSelf = customVertexStream }, (evt) =>
            {
                if (Equals(vertexStream, evt.newValue))
                    return;

                registerUndo("Change Vertex Stream");
                vertexStream = (CustomVertexStream)evt.newValue;
                onChange();
            });

            universalTarget.AddDefaultMaterialOverrideGUI(ref context, onChange, registerUndo);
            universalTarget.AddDefaultSurfacePropertiesGUI(ref context, onChange, registerUndo, showReceiveShadows: false);
        }

        public bool TryUpgradeFromMasterNode(IMasterNode1 masterNode, out Dictionary<BlockFieldDescriptor, int> blockMap)
        {
            blockMap = null;
            if (!(masterNode is UnlitMasterNode1 unlitMasterNode))
                return false;

            // Set blockmap
            blockMap = new Dictionary<BlockFieldDescriptor, int>()
            {
                { BlockFields.VertexDescription.Position, 9 },
                { BlockFields.VertexDescription.Normal, 10 },
                { BlockFields.VertexDescription.Tangent, 11 },
                { BlockFields.SurfaceDescription.BaseColor, 0 },
                { BlockFields.SurfaceDescription.Alpha, 7 },
                { BlockFields.SurfaceDescription.AlphaClipThreshold, 8 },
            };

            return true;
        }

        internal override void OnAfterParentTargetDeserialized()
        {
            Assert.IsNotNull(target);

            if (this.sgVersion < latestVersion)
            {
                // Upgrade old incorrect Premultiplied blend (with alpha multiply in shader) into
                // equivalent Alpha blend mode for backwards compatibility.
                if (this.sgVersion < 1)
                {
                    if (target.alphaMode == AlphaMode.Premultiply)
                    {
                        target.alphaMode = AlphaMode.Alpha;
                    }
                }
                ChangeVersion(latestVersion);
            }
        }

        #region SubShader
        static class SubShaders
        {
            public static SubShaderDescriptor Unlit(UniversalTarget target, string renderType, string renderQueue, string disableBatchingTag, bool customVertexStream = false, CustomVertexStream vertexStream = CustomVertexStream.None)
            {
                var instancingPragmas = new PragmaCollection { CorePragmas.Instanced, { new PragmaDescriptor { value = $"instancing_options procedural:{particleInstancingFuncName}" } } };

                var result = new SubShaderDescriptor()
                {
                    pipelineTag = UniversalTarget.kPipelineTag,
                    customTags = UniversalTarget.kUnlitMaterialTypeTag,
                    renderType = renderType,
                    renderQueue = renderQueue,
                    disableBatchingTag = disableBatchingTag,
                    generatesPreview = true,
                    passes = new PassCollection()
                };

                result.passes.Add(UnlitPasses.Forward(target, UnlitKeywords.Forward, customVertexStream, vertexStream));

                if (target.mayWriteDepth)
                    result.passes.Add(PassVariant(CorePasses.DepthOnly(target), CorePragmas.Instanced));

                if (target.alwaysRenderMotionVectors)
                    result.customTags = string.Concat(result.customTags, " ", UniversalTarget.kAlwaysRenderMotionVectorsTag);
                result.passes.Add(PassVariant(CorePasses.MotionVectors(target), CorePragmas.MotionVectors));

                if (IsSpacewarpSupported())
                    result.passes.Add(PassVariant(CorePasses.XRMotionVectors(target), CorePragmas.XRMotionVectors));

                //result.passes.Add(PassVariant(UnlitPasses.DepthNormalOnly(target, customVertexStream, vertexStream), CorePragmas.Instanced));
                result.passes.Add(PassVariant(UnlitPasses.DepthNormalOnly(target, customVertexStream, vertexStream), instancingPragmas));

                if (target.castShadows || target.allowMaterialOverride)
                    result.passes.Add(PassVariant(CorePasses.ShadowCaster(target), CorePragmas.Instanced));

                // Fill GBuffer with color and normal for custom GBuffer use cases.
                result.passes.Add(UnlitPasses.GBuffer(target, customVertexStream, vertexStream));

                // Currently neither of these passes (selection/picking) can be last for the game view for
                // UI shaders to render correctly. Verify [1352225] before changing this order.

                result.passes.Add(PassVariant(SceneSelection(target, customVertexStream, vertexStream), instancingPragmas));
                result.passes.Add(PassVariant(ScenePicking(target, customVertexStream, vertexStream), instancingPragmas));

                return result;
            }
        }
        #endregion

        #region Pass
        internal static void AddAlphaClipControlToPass(ref PassDescriptor pass, UniversalTarget target)
        {
            if (target.allowMaterialOverride)
                pass.keywords.Add(CoreKeywordDescriptors.AlphaTestOn);
            else if (target.alphaClip)
                pass.defines.Add(CoreKeywordDescriptors.AlphaTestOn, 1);
        }

        static PassDescriptor SceneSelection(UniversalTarget target, bool customVertexStream = false, CustomVertexStream vertexStream = CustomVertexStream.None)
        {
            var graphIncludes = new IncludeCollection { CoreIncludes.SceneSelection , { particleInstancingIncludePath, IncludeLocation.Pregraph } };

            StructCollection structCollection = customVertexStream ?
                new StructCollection { ParticleStruct(vertexStream), CoreStructCollections.Default }:
                CoreStructCollections.Default;

            var defines = customVertexStream ?
                new DefineCollection { CoreDefines.SceneSelection, { CoreKeywordDescriptors.AlphaClipThreshold, 1 }, { CustomParticleDataDefine(vertexStream), 1 } } :
                new DefineCollection { CoreDefines.SceneSelection, { CoreKeywordDescriptors.AlphaClipThreshold, 1 } };

            var result = new PassDescriptor()
            {
                // Definition
                displayName = "SceneSelectionPass",
                referenceName = "SHADERPASS_DEPTHONLY",
                lightMode = "SceneSelectionPass",
                useInPreview = false,

                // Template
                passTemplatePath = UniversalTarget.kUberTemplatePath,
                sharedTemplateDirectories = UniversalTarget.kSharedTemplateDirectories,

                // Port Mask
                validVertexBlocks = CoreBlockMasks.Vertex,
                validPixelBlocks = CoreBlockMasks.FragmentAlphaOnly,

                // Fields
                structs = structCollection,
                fieldDependencies = CoreFieldDependencies.Default,

                // Conditional State
                renderStates = CoreRenderStates.SceneSelection(target),
                pragmas = CorePragmas.Instanced,
                defines = defines,
                keywords = new KeywordCollection(),
                includes = graphIncludes,

                // Custom Interpolator Support
                customInterpolators = CoreCustomInterpDescriptors.Common
            };

            AddAlphaClipControlToPass(ref result, target);

            return result;
        }

        static PassDescriptor ScenePicking(UniversalTarget target, bool customVertexStream = false, CustomVertexStream vertexStream = CustomVertexStream.None)
        {
            var graphIncludes = new IncludeCollection { CoreIncludes.ScenePicking , { particleInstancingIncludePath, IncludeLocation.Pregraph } };

            StructCollection structCollection = customVertexStream ?
                new StructCollection { ParticleStruct(vertexStream), CoreStructCollections.Default }:
                CoreStructCollections.Default;

            var defines = customVertexStream ?
                new DefineCollection { CoreDefines.ScenePicking, { CoreKeywordDescriptors.AlphaClipThreshold, 1 }, { CustomParticleDataDefine(vertexStream), 1 } } :
                new DefineCollection { CoreDefines.ScenePicking, { CoreKeywordDescriptors.AlphaClipThreshold, 1 } };

            var result = new PassDescriptor()
            {
                // Definition
                displayName = "ScenePickingPass",
                referenceName = "SHADERPASS_DEPTHONLY",
                lightMode = "Picking",
                useInPreview = false,

                // Template
                passTemplatePath = UniversalTarget.kUberTemplatePath,
                sharedTemplateDirectories = UniversalTarget.kSharedTemplateDirectories,

                // Port Mask
                validVertexBlocks = CoreBlockMasks.Vertex,
                // NB Color is not strictly needed for scene picking but adding it here so that there are nodes to be
                // collected for the pixel shader. Some packages might use this to customize the scene picking rendering.
                validPixelBlocks = CoreBlockMasks.FragmentColorAlpha,

                // Fields
                structs = structCollection,
                fieldDependencies = CoreFieldDependencies.Default,

                // Conditional State
                renderStates = CoreRenderStates.ScenePicking(target),
                //pragmas = procedural ? new PragmaCollection { { CorePragmas.Instanced }, { new PragmaDescriptor { value = $"instancing_options procedural:{pFunctionName}" } } } : CorePragmas.Instanced,
                pragmas = CorePragmas.Instanced,
                defines = defines,
                keywords = new KeywordCollection(),
                includes = graphIncludes,

                // Custom Interpolator Support
                customInterpolators = CoreCustomInterpDescriptors.Common
            };

            AddAlphaClipControlToPass(ref result, target);

            return result;
        }

        static class UnlitPasses
        {
            public static PassDescriptor Forward(UniversalTarget target, KeywordCollection keywords, bool customVertexStream = false, CustomVertexStream vertexStream = CustomVertexStream.None)
            {
                var graphIncludes = new IncludeCollection { UnlitIncludes.Forward , { particleInstancingIncludePath, IncludeLocation.Pregraph } };

                StructCollection structCollection = customVertexStream ?
                    new StructCollection { ParticleStruct(vertexStream), CoreStructCollections.Default }:
                    CoreStructCollections.Default;

                var defines = customVertexStream ?
                    new DefineCollection { CoreDefines.UseFragmentFog , { CustomParticleDataDefine(vertexStream), 1 } } :
                    CoreDefines.UseFragmentFog;

                var result = new PassDescriptor
                {
                    // Definition
                    displayName = "Universal Forward",
                    referenceName = "SHADERPASS_UNLIT",
                    useInPreview = true,

                    // Template
                    passTemplatePath = UniversalTarget.kUberTemplatePath,
                    sharedTemplateDirectories = UniversalTarget.kSharedTemplateDirectories,

                    // Port Mask
                    validVertexBlocks = CoreBlockMasks.Vertex,
                    validPixelBlocks = CoreBlockMasks.FragmentColorAlpha,

                    // Fields
                    structs = structCollection,
                    requiredFields = UnlitRequiredFields.Unlit,
                    fieldDependencies = CoreFieldDependencies.Default,

                    // Conditional State
                    renderStates = CoreRenderStates.UberSwitchedRenderState(target),
                    pragmas = new PragmaCollection { CorePragmas.Forward, ProceduralPragma() }, //ForwardProcedural(particleInstancingFuncName),
                    defines = defines, //new DefineCollection { CoreDefines.UseFragmentFog },
                    keywords = new KeywordCollection { keywords },
                    includes = graphIncludes,

                    // Additional Commands
                    //additionalCommands = c,

                    // Custom Interpolator Support
                    customInterpolators = CoreCustomInterpDescriptors.Common
                };

                CorePasses.AddTargetSurfaceControlsToPass(ref result, target);
                CorePasses.AddAlphaToMaskControlToPass(ref result, target);
                CorePasses.AddLODCrossFadeControlToPass(ref result, target);

                return result;
            }

            public static PassDescriptor DepthNormalOnly(UniversalTarget target, bool customVertexStream = false, CustomVertexStream vertexStream = CustomVertexStream.None)
            {
                var graphIncludes = new IncludeCollection { CoreIncludes.DepthNormalsOnly , { particleInstancingIncludePath, IncludeLocation.Pregraph } };

                StructCollection structCollection = customVertexStream ?
                    new StructCollection { ParticleStruct(vertexStream), CoreStructCollections.Default } :
                    CoreStructCollections.Default;

                var defines = customVertexStream ?
                    new DefineCollection { { CustomParticleDataDefine(vertexStream), 1 } } :
                    new DefineCollection();

                var result = new PassDescriptor
                {
                    // Definition
                    displayName = "DepthNormalsOnly",
                    referenceName = "SHADERPASS_DEPTHNORMALSONLY",
                    lightMode = "DepthNormalsOnly",
                    useInPreview = true,

                    // Template
                    passTemplatePath = UniversalTarget.kUberTemplatePath,
                    sharedTemplateDirectories = UniversalTarget.kSharedTemplateDirectories,

                    // Port Mask
                    validVertexBlocks = CoreBlockMasks.Vertex,
                    validPixelBlocks = UnlitBlockMasks.FragmentDepthNormals,

                    // Fields
                    structs = structCollection,
                    requiredFields = UnlitRequiredFields.DepthNormalsOnly,
                    fieldDependencies = CoreFieldDependencies.Default,

                    // Conditional State
                    renderStates = CoreRenderStates.DepthNormalsOnly(target),
                    pragmas = new PragmaCollection { CorePragmas.Forward, ProceduralPragma() },//ForwardProcedural(particleInstancingFuncName),
                    defines = defines,
                    keywords = new KeywordCollection { CoreKeywordDescriptors.GBufferNormalsOct },
                    includes = graphIncludes,

                    // Custom Interpolator Support
                    customInterpolators = CoreCustomInterpDescriptors.Common
                };

                CorePasses.AddTargetSurfaceControlsToPass(ref result, target);
                CorePasses.AddLODCrossFadeControlToPass(ref result, target);

                return result;
            }

            // Deferred only in SM4.5
            // GBuffer fill for consistency.
            public static PassDescriptor GBuffer(UniversalTarget target, bool customVertexStream = false, CustomVertexStream vertexStream = CustomVertexStream.None)
            {
                var graphIncludes = new IncludeCollection { UnlitIncludes.GBuffer, { particleInstancingIncludePath, IncludeLocation.Pregraph } };

                StructCollection structCollection = customVertexStream ?
                    new StructCollection { ParticleStruct(vertexStream), CoreStructCollections.Default } :
                    CoreStructCollections.Default;

                var defines = customVertexStream ?
                    new DefineCollection { { CustomParticleDataDefine(vertexStream), 1 } } :
                    new DefineCollection();

                var result = new PassDescriptor
                {
                    // Definition
                    displayName = "GBuffer",
                    referenceName = "SHADERPASS_GBUFFER",
                    lightMode = "UniversalGBuffer",
                    useInPreview = true,

                    // Template
                    passTemplatePath = UniversalTarget.kUberTemplatePath,
                    sharedTemplateDirectories = UniversalTarget.kSharedTemplateDirectories,

                    // Port Mask
                    validVertexBlocks = CoreBlockMasks.Vertex,
                    validPixelBlocks = CoreBlockMasks.FragmentColorAlpha,

                    // Fields
                    structs = structCollection,
                    requiredFields = UnlitRequiredFields.GBuffer,
                    fieldDependencies = CoreFieldDependencies.Default,

                    // Conditional State
                    renderStates = CoreRenderStates.UberSwitchedRenderState(target),
                    pragmas = new PragmaCollection { CorePragmas.GBuffer , ProceduralPragma() },
                    defines = defines,
                    keywords = new KeywordCollection { UnlitKeywords.GBuffer },
                    includes = graphIncludes,

                    // Custom Interpolator Support
                    customInterpolators = CoreCustomInterpDescriptors.Common
                };

                CorePasses.AddTargetSurfaceControlsToPass(ref result, target);
                CorePasses.AddLODCrossFadeControlToPass(ref result, target);

                return result;
            }

            #region PortMasks
            static class UnlitBlockMasks
            {
                public static readonly BlockFieldDescriptor[] FragmentDepthNormals = new BlockFieldDescriptor[]
                {
                    BlockFields.SurfaceDescription.NormalWS,
                    BlockFields.SurfaceDescription.Alpha,
                    BlockFields.SurfaceDescription.AlphaClipThreshold,
                };
            }
            #endregion

            #region RequiredFields
            static class UnlitRequiredFields
            {
                public static readonly FieldCollection Unlit = new FieldCollection()
                {
                    StructFields.Varyings.positionWS,
                    StructFields.Varyings.normalWS
                };

                public static readonly FieldCollection DepthNormalsOnly = new FieldCollection()
                {
                    StructFields.Varyings.normalWS,
                };

                public static readonly FieldCollection GBuffer = new FieldCollection()
                {
                    StructFields.Varyings.positionWS,
                    StructFields.Varyings.normalWS,
                    UniversalStructFields.Varyings.sh,   // Satisfy !LIGHTMAP_ON requirements.
                    UniversalStructFields.Varyings.probeOcclusion,
                };
            }
            #endregion
        }
        #endregion

        #region Keywords
        static class UnlitKeywords
        {
            public static readonly KeywordCollection Forward = new KeywordCollection()
            {
                // This contain lightmaps because without a proper custom lighting solution in Shadergraph,
                // people start with the unlit then add lightmapping nodes to it.
                // If we removed lightmaps from the unlit target this would ruin a lot of peoples days.
                CoreKeywordDescriptors.StaticLightmap,
                CoreKeywordDescriptors.DirectionalLightmapCombined,
                CoreKeywordDescriptors.UseLegacyLightmaps,
                CoreKeywordDescriptors.LightmapBicubicSampling,
                CoreKeywordDescriptors.SampleGI,
                CoreKeywordDescriptors.DBuffer,
                CoreKeywordDescriptors.DebugDisplay,
                CoreKeywordDescriptors.ScreenSpaceAmbientOcclusion,
            };

            public static readonly KeywordCollection GBuffer = new KeywordCollection
            {
                CoreKeywordDescriptors.DBuffer,
                CoreKeywordDescriptors.ScreenSpaceAmbientOcclusion,
                CoreKeywordDescriptors.RenderPassEnabled,
            };
        }
        #endregion

        #region Includes
        static class UnlitIncludes
        {
            const string kUnlitPass = "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/UnlitPass.hlsl";
            const string kUnlitGBufferPass = "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/UnlitGBufferPass.hlsl";

            public static IncludeCollection Forward = new IncludeCollection
            {
                // Pre-graph
                { CoreIncludes.DOTSPregraph },
                { CoreIncludes.FogPregraph },
                { CoreIncludes.WriteRenderLayersPregraph },
                { CoreIncludes.CorePregraph },
                { CoreIncludes.ShaderGraphPregraph },
                { CoreIncludes.DBufferPregraph },
                { CoreIncludes.WriteRenderLayersPregraph },

                // Post-graph
                { CoreIncludes.CorePostgraph },
                { kUnlitPass, IncludeLocation.Postgraph },
            };

            public static IncludeCollection GBuffer = new IncludeCollection
            {
                // Pre-graph
                { CoreIncludes.DOTSPregraph },
                { CoreIncludes.CorePregraph },
                { CoreIncludes.ShaderGraphPregraph },
                { CoreIncludes.DBufferPregraph },
                { CoreIncludes.WriteRenderLayersPregraph },

                // Post-graph
                { CoreIncludes.CorePostgraph },
                { kUnlitGBufferPass, IncludeLocation.Postgraph },
            };
        }
        #endregion
    }
}
