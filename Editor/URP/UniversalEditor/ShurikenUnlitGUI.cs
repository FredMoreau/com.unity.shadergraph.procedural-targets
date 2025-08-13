using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using ParticleGUIStyles = UnityEditor.Rendering.Universal.ShaderGUI.ParticleGUI.Styles;

namespace UnityEditor.Rendering.Universal.ShaderGUI
{
    class ShurikenUnlitGUI : ShaderGraphUnlitGUI
    {
        private static ReorderableList vertexStreamList;

        List<ParticleSystemRenderer> m_RenderersUsingThisMaterial = new List<ParticleSystemRenderer>();

        public override void DrawAdvancedOptions(Material material)
        {
            base.DrawAdvancedOptions(material);

            //materialEditor.ShaderProperty(particleProps.flipbookMode, ParticleGUI.Styles.flipbookMode);
            //ParticleGUI.FadingOptions(material, materialEditor, particleProps);
            /*ParticleGUI.*/DoVertexStreamsArea(material, m_RenderersUsingThisMaterial);

            //DrawQueueOffsetField();
        }

        public override void OnOpenGUI(Material material, MaterialEditor materialEditor)
        {
            CacheRenderersUsingThisMaterial(material);
            base.OnOpenGUI(material, materialEditor);
        }

        void CacheRenderersUsingThisMaterial(Material material)
        {
            m_RenderersUsingThisMaterial.Clear();

            ParticleSystemRenderer[] renderers = UnityEngine.Object.FindObjectsByType<ParticleSystemRenderer>(FindObjectsSortMode.InstanceID);
            foreach (ParticleSystemRenderer renderer in renderers)
            {
                if (renderer.sharedMaterial == material)
                    m_RenderersUsingThisMaterial.Add(renderer);
            }
        }

        static void DoVertexStreamsArea(Material material, List<ParticleSystemRenderer> renderers, bool useLighting = false)
        {
            EditorGUILayout.Space();
            // Display list of streams required to make this shader work
            bool useNormalMap = false;
            bool useFlipbookBlending = material.HasFloat("_FlipbookBlending") && (material.GetFloat("_FlipbookBlending") > 0.0f);
            if (material.HasProperty("_BumpMap"))
                useNormalMap = material.GetTexture("_BumpMap");

            bool useGPUInstancing = ShaderUtil.HasProceduralInstancing(material.shader);
            if (useGPUInstancing && renderers.Count > 0)
            {
                if (!renderers[0].enableGPUInstancing || renderers[0].renderMode != ParticleSystemRenderMode.Mesh)
                    useGPUInstancing = false;
            }

            // Build the list of expected vertex streams
            List<ParticleSystemVertexStream> streams = new List<ParticleSystemVertexStream>();
            List<string> streamList = new List<string>();

            streams.Add(ParticleSystemVertexStream.Position);
            streamList.Add(ParticleGUIStyles.streamPositionText);

            if (useLighting || useNormalMap)
            {
                streams.Add(ParticleSystemVertexStream.Normal);
                streamList.Add(ParticleGUIStyles.streamNormalText);
                if (useNormalMap)
                {
                    streams.Add(ParticleSystemVertexStream.Tangent);
                    streamList.Add(ParticleGUIStyles.streamTangentText);
                }
            }

            streams.Add(ParticleSystemVertexStream.Color);
            streamList.Add(useGPUInstancing ? ParticleGUIStyles.streamColorInstancedText : ParticleGUIStyles.streamColorText);
            streams.Add(ParticleSystemVertexStream.UV);
            streamList.Add(ParticleGUIStyles.streamUVText);

            List<ParticleSystemVertexStream> instancedStreams = new List<ParticleSystemVertexStream>(streams);

            if (useGPUInstancing)
            {
                instancedStreams.Add(ParticleSystemVertexStream.AnimFrame);
                streamList.Add(ParticleGUIStyles.streamAnimFrameText);
            }
            else if (useFlipbookBlending && !useGPUInstancing)
            {
                streams.Add(ParticleSystemVertexStream.UV2);
                streamList.Add(ParticleGUIStyles.streamUV2Text);
                streams.Add(ParticleSystemVertexStream.AnimBlend);
                streamList.Add(ParticleGUIStyles.streamAnimBlendText);
            }

            vertexStreamList = new ReorderableList(streamList, typeof(string), false, true, false, false);

            vertexStreamList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, ParticleGUIStyles.VertexStreams);
            };

            vertexStreamList.DoLayoutList();

            // Display a warning if any renderers have incorrect vertex streams
            string Warnings = "";
            List<ParticleSystemVertexStream> rendererStreams = new List<ParticleSystemVertexStream>();
            foreach (ParticleSystemRenderer renderer in renderers)
            {
                renderer.GetActiveVertexStreams(rendererStreams);

                bool streamsValid;
                if (useGPUInstancing && renderer.renderMode == ParticleSystemRenderMode.Mesh && renderer.supportsMeshInstancing)
                    streamsValid = CompareVertexStreams(rendererStreams, instancedStreams);
                else
                    streamsValid = CompareVertexStreams(rendererStreams, streams);

                if (!streamsValid)
                    Warnings += "-" + renderer.name + "\n";
            }

            if (!string.IsNullOrEmpty(Warnings))
            {
                EditorGUILayout.HelpBox(
                    "The following Particle System Renderers are using this material with incorrect Vertex Streams:\n" +
                    Warnings, MessageType.Error, true);
                // Set the streams on all systems using this material
                if (GUILayout.Button(ParticleGUIStyles.streamApplyToAllSystemsText, EditorStyles.miniButton, GUILayout.ExpandWidth(true)))
                {
                    Undo.RecordObjects(renderers.Where(r => r != null).ToArray(), ParticleGUIStyles.undoApplyCustomVertexStreams);

                    foreach (ParticleSystemRenderer renderer in renderers)
                    {
                        if (useGPUInstancing && renderer.renderMode == ParticleSystemRenderMode.Mesh && renderer.supportsMeshInstancing)
                            renderer.SetActiveVertexStreams(instancedStreams);
                        else
                            renderer.SetActiveVertexStreams(streams);
                    }
                }
            }
        }
        private static bool CompareVertexStreams(IEnumerable<ParticleSystemVertexStream> a, IEnumerable<ParticleSystemVertexStream> b)
        {
            var differenceA = a.Except(b);
            var differenceB = b.Except(a);
            var difference = differenceA.Union(differenceB).Distinct();
            if (!difference.Any())
                return true;
            // If normals are the only difference, ignore them, because the default particle streams include normals, to make it easy for users to switch between lit and unlit
            if (difference.Count() == 1)
            {
                if (difference.First() == ParticleSystemVertexStream.Normal)
                    return true;
            }
            return false;
        }
    }
}
