﻿using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using GraphProcessor;
using System;
using System.Linq;
using Object = UnityEngine.Object;
using UnityEngine.Experimental.Rendering;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Rendering;
#endif

namespace Mixture
{
	public abstract class MixtureNode : BaseNode
	{
		protected new MixtureGraph			graph => base.graph as MixtureGraph;

		public MixtureRTSettings			rtSettings;

		protected virtual MixtureRTSettings	defaultRTSettings => MixtureRTSettings.defaultValue;
		public virtual float   				nodeWidth => MixtureUtils.defaultNodeWidth;
		public virtual Texture				previewTexture => null;
		public virtual bool					hasSettings => true;
		public virtual List<OutputDimension> supportedDimensions => new List<OutputDimension>() {
			OutputDimension.Texture2D,
			OutputDimension.Texture3D,
			OutputDimension.CubeMap,
		};
		public virtual PreviewChannels		defaultPreviewChannels => PreviewChannels.RGBA;
		[SerializeField]
		public bool							isPreviewCollapsed = false;

		public virtual bool					showDefaultInspector => false;

		public event Action					onSettingsChanged;

        // UI Serialization
        [SerializeField]
        public PreviewChannels previewMode;
        [SerializeField]
        public float previewMip = 0.0f;
        [SerializeField]
        public bool previewVisible = true;

        public override void OnNodeCreated()
		{
			base.OnNodeCreated();
			rtSettings = defaultRTSettings;
			previewMode = defaultPreviewChannels;
		}

		protected bool UpdateTempRenderTexture(ref CustomRenderTexture target, bool hasMips = false, bool autoGenerateMips = false)
		{
			if (graph.outputTexture == null)
				return false;

			int outputWidth = rtSettings.GetWidth(graph);
			int outputHeight = rtSettings.GetHeight(graph);
			int outputDepth = rtSettings.GetDepth(graph);
			GraphicsFormat targetFormat = rtSettings.GetGraphicsFormat(graph);
			TextureDimension dimension = rtSettings.GetTextureDimension(graph);
			
			if (dimension == TextureDimension.Cube)
				outputHeight = outputDepth = outputWidth; // we only use the width for cubemaps

            if (targetFormat == GraphicsFormat.None)
                targetFormat = graph.outputTexture.graphicsFormat;
			if (dimension == TextureDimension.None)
				dimension = TextureDimension.Tex2D;

			if (target == null)
			{
                target = new CustomRenderTexture(outputWidth, outputHeight, targetFormat)
                {
                    volumeDepth = Math.Max(1, outputDepth),
                    dimension = dimension,
                    name = $"Mixture Temp {name}",
                    updateMode = CustomRenderTextureUpdateMode.OnDemand,
                    doubleBuffered = rtSettings.doubleBuffered,
                    wrapMode = rtSettings.wrapMode,
                    filterMode = rtSettings.filterMode,
                    useMipMap = hasMips,
					autoGenerateMips = autoGenerateMips,
				};
				target.Create();

				return true;
			}

			// TODO: check if format is supported by current system

			// Warning: here we use directly the settings from the 
			if (target.width != outputWidth
				|| target.height != outputHeight
				|| target.graphicsFormat != targetFormat
				|| target.dimension != dimension
				|| target.volumeDepth != outputDepth
				|| target.filterMode != graph.outputTexture.filterMode
				|| target.doubleBuffered != rtSettings.doubleBuffered
                || target.wrapMode != rtSettings.wrapMode
                || target.filterMode != rtSettings.filterMode
				|| target.useMipMap != hasMips
				|| target.autoGenerateMips != autoGenerateMips)
			{
				target.Release();
				target.width = Math.Max(1, outputWidth);
				target.height = Math.Max(1, outputHeight);
				target.graphicsFormat = (GraphicsFormat)targetFormat;
				target.dimension = (TextureDimension)dimension;
				target.volumeDepth = outputDepth;
				target.doubleBuffered = rtSettings.doubleBuffered;
                target.wrapMode = rtSettings.wrapMode;
                target.filterMode = rtSettings.filterMode;
                target.useMipMap = hasMips;
				target.autoGenerateMips = autoGenerateMips;
				target.Create();
			}

			return false;
		}

		protected sealed override void Process()
		{
			var outputDimension = rtSettings.GetTextureDimension(graph);

			if (!supportedDimensions.Contains((OutputDimension)outputDimension))
			{
				AddMessage($"Dimension {outputDimension} is not supported by this node", NodeMessageType.Error);
				return ;
			}
			else
			{
				// TODO: simplify this with the node graph processor remove badges with matching words feature
				RemoveMessage($"Dimension {TextureDimension.Tex2D} is not supported by this node");
				RemoveMessage($"Dimension {TextureDimension.Tex3D} is not supported by this node");
				RemoveMessage($"Dimension {TextureDimension.Cube} is not supported by this node");
			}

			ProcessNode();
		}

		protected virtual bool ProcessNode() => true;

		protected void AddObjectToGraph(Object obj) => graph.AddObjectToGraph(obj);
		protected void RemoveObjectFromGraph(Object obj) => graph.RemoveObjectFromGraph(obj);

		protected Type GetPropertyType(Shader s, int index)
		{
			switch (s.GetPropertyType(index))
			{
				case ShaderPropertyType.Color:
					return typeof(Color);
				case ShaderPropertyType.Float:
				case ShaderPropertyType.Range:
					return typeof(float);
				case ShaderPropertyType.Texture:
					return TextureUtils.GetTypeFromDimension(s.GetPropertyTextureDimension(index));
				default:
				case ShaderPropertyType.Vector:
					return typeof(Vector4);
			}
		}

		protected IEnumerable< PortData > GetMaterialPortDatas(Material material)
		{
			if (material == null)
				yield break;

			var currentDimension = rtSettings.GetTextureDimension(graph);

			var s = material.shader;
				
			for (int i = 0; i < s.GetPropertyCount(); i++)
			{
				var flags = s.GetPropertyFlags(i);
				if (flags == ShaderPropertyFlags.HideInInspector
					|| flags == ShaderPropertyFlags.NonModifiableTextureData
					|| flags == ShaderPropertyFlags.PerRendererData)
					continue;
				
				if (!PropertySupportsDimension(s.GetPropertyName(i), currentDimension))
					continue;

				yield return new PortData{
					identifier = s.GetPropertyName(i),
					displayName = s.GetPropertyDescription(i),
					displayType = GetPropertyType(s, i),
				};
			}
		}

		bool PropertySupportsDimension(string name, TextureDimension dim)
		{
			return MixtureUtils.GetAllowedDimentions(name).Contains(dim);
		}

		protected void AssignMaterialPropertiesFromEdges(List< SerializableEdge > edges, Material material)
		{
			// Update material settings when processing the graph:
			foreach (var edge in edges)
			{
				var propName = edge.inputPort.portData.identifier;
				int i = material.shader.FindPropertyIndex(propName);
				var type = material.shader.GetPropertyType(i);

				switch (type)
				{
					case ShaderPropertyType.Color:
						material.SetColor(propName, (Color)edge.passThroughBuffer);
						break;
					case ShaderPropertyType.Texture:
						// TODO: texture scale and offset
						material.SetTexture(propName, (Texture)edge.passThroughBuffer);
						break;
					case ShaderPropertyType.Float:
					case ShaderPropertyType.Range:
						switch (edge.passThroughBuffer)
						{
							case float f:
								material.SetFloat(propName, f);
								break;
							case Vector2 v:
								material.SetFloat(propName, v.x);
								break;
							case Vector3 v:
								material.SetFloat(propName, v.x);
								break;
							case Vector4 v:
								material.SetFloat(propName, v.x);
								break;
							default:
								throw new Exception($"Can't assign {edge.passThroughBuffer.GetType()} to material float property");
						}
						break;
					case ShaderPropertyType.Vector:
						material.SetVector(propName, MixtureConversions.ConvertObjectToVector4(edge.passThroughBuffer));
						break;
				}
			}
		}

#if UNITY_EDITOR
		protected bool IsShaderCompiled(Shader s)
		{
			return !ShaderUtil.GetShaderMessages(s).Any(m => m.severity == ShaderCompilerMessageSeverity.Error);
		}

		protected void LogShaderErrors(Shader s)
		{
			foreach (var m in ShaderUtil.GetShaderMessages(s).Where(m => m.severity == ShaderCompilerMessageSeverity.Error))
			{
				string file = String.IsNullOrEmpty(m.file) ? s.name : m.file;
				Debug.LogError($"{file}:{m.line} {m.message} {m.messageDetails}");
			}
		}

		public void OnSettingsChanged() => onSettingsChanged?.Invoke();
#endif

		Dictionary<Material, Material>		defaultMaterials = new Dictionary<Material, Material>();

		public Material	GetDefaultMaterial(Material mat)
		{
			Material defaultMat;

			if (defaultMaterials.TryGetValue(mat, out defaultMat))
				return defaultMat;
			
			return defaultMaterials[mat] = new Material(mat.shader);
		}

		public void ResetMaterialPropertyToDefault(Material mat, string propName)
		{
			if (mat == null)
				return;

			int idx = mat.shader.FindPropertyIndex(propName);
			switch (mat.shader.GetPropertyType(idx))
			{
				case ShaderPropertyType.Float:
				case ShaderPropertyType.Range:
					mat.SetFloat(propName, GetDefaultMaterial(mat).GetFloat(propName));
					break;
				case ShaderPropertyType.Vector:
					mat.SetVector(propName, GetDefaultMaterial(mat).GetVector(propName));
					break;
				case ShaderPropertyType.Texture:
					mat.SetTexture(propName, GetDefaultMaterial(mat).GetTexture(propName));
					break;
			}
		}
	}

	[Flags]
	public enum EditFlags
	{
		None			= 0,
		Width			= 1 << 0,
		WidthMode		= 1 << 1,
		Height			= 1 << 2,
		HeightMode		= 1 << 3,
		Depth			= 1 << 4,
		DepthMode		= 1 << 5,
		Dimension		= 1 << 6,
		TargetFormat	= 1 << 7,
		POTSize			= 1 << 8,
		All				= ~0,
	}

	public enum POTSize
	{
		_32			= 32,
		_64			= 64,
		_128		= 128,
		_256		= 256,
		_512		= 512,
		_1024		= 1024,
		_2048		= 2048,
		_4096		= 4096,
		_8192		= 8192,
		Custom		= -1,
	}

	public enum OutputSizeMode
	{
		Default = 0,
		Fixed = 1,
		PercentageOfOutput = 2
	}

	public enum OutputDimension
	{
		Default = TextureDimension.None,
		Texture2D = TextureDimension.Tex2D,
		CubeMap = TextureDimension.Cube,
		Texture3D = TextureDimension.Tex3D,
		// Texture2DArray = TextureDimension.Tex2DArray, // Not supported by CRT, will be handled as Texture3D and then saved as Tex2DArray
	}

	public enum OutputFormat
	{
		Default = GraphicsFormat.None,
		RGBA_LDR = GraphicsFormat.R8G8B8A8_UNorm,
		RGBA_sRGB = GraphicsFormat.R8G8B8A8_SRGB,
		RGBA_Half = GraphicsFormat.R16G16B16A16_SFloat,
		RGBA_Float = GraphicsFormat.R32G32B32A32_SFloat,
		R8_Unsigned = GraphicsFormat.R8_UNorm,
		R16 = GraphicsFormat.R16_SFloat,
	}

	[Flags]
    public enum PreviewChannels
    {
        R = 1,
        G = 2,
        B = 4,
        A = 8,
        RG = R | G,
        RB = R | B,
        GB = G | B,
        RGB = R | G | B,
        RGBA = R | G | B | A,
    }

	// Note: to keep in sync with UnityEditor.TextureCompressionQuality
	public enum MixtureCompressionQuality
	{
    	Fast     = 0,
    	Normal   = 50,
    	Best     = 100,
	}
}