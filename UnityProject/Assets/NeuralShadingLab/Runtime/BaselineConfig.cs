using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NeuralShadingLab
{
    [CreateAssetMenu(menuName = "Neural Shading Lab/Baseline Configuration")]
    public sealed class BaselineConfig : ScriptableObject
    {
        public string scenePath;
        public string experiment = "smoke-test";
        public bool smokeTest = true;
        public int warmupFrames = 300;
        public int sampleCount = 1000;
        public int simulationRate = 60;
        public int width = 1920;
        public int height = 1080;
        public int randomSeed = 12345;
        public int qualityLevel = -1;
        public UniversalRenderPipelineAsset pipelineOverride;
        public CameraPose[] cameraPoses = Array.Empty<CameraPose>();
        [Tooltip("Component type names of camera controllers to disable during benchmark only.")]
        public string[] disableComponentTypes = Array.Empty<string>();
    }

    [Serializable]
    public struct CameraPose
    {
        public Vector3 position;
        public Quaternion rotation;
        public float fieldOfView;
    }
}
