using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Voltage;
using Voltage.Data;
using Voltage.Systems;
using Voltage.Textures;
using Voltage.Utils;
using Voltage.Utils.Collections;
using Voltage.Utils.Extensions;
using System.IO;
using System.Linq;
using Voltage.Serialization;


namespace Voltage;

public class Scene
{
	public enum SceneResolutionPolicy
	{
		/// <summary>
		/// Default. RenderTarget matches the sceen size
		/// </summary>
		None,

		/// <summary>
		/// The entire application is visible in the specified area without trying to preserve the original aspect ratio.
		/// Distortion can occur, and the application may appear stretched or compressed.
		/// </summary>
		ExactFit,

		/// <summary>
		/// The entire application fills the specified area, without distortion but possibly with some cropping,
		/// while maintaining the original aspect ratio of the application.
		/// </summary>
		NoBorder,

		/// <summary>
		/// Pixel perfect version of NoBorder. Scaling is limited to integer values.
		/// </summary>
		NoBorderPixelPerfect,

		/// <summary>
		/// The entire application is visible in the specified area without distortion while maintaining the original
		/// aspect ratio of the application. Borders can appear on two sides of the application.
		/// </summary>
		ShowAll,

		/// <summary>
		/// Pixel perfect version of ShowAll. Scaling is limited to integer values.
		/// </summary>
		ShowAllPixelPerfect,

		/// <summary>
		/// The application takes the height of the design resolution size and modifies the width of the internal
		/// canvas so that it fits the aspect ratio of the device.
		/// no distortion will occur however you must make sure your application works on different
		/// aspect ratios
		/// </summary>
		FixedHeight,

		/// <summary>
		/// Pixel perfect version of FixedHeight. Scaling is limited to integer values.
		/// </summary>
		FixedHeightPixelPerfect,

		/// <summary>
		/// The application takes the width of the design resolution size and modifies the height of the internal
		/// canvas so that it fits the aspect ratio of the device.
		/// no distortion will occur however you must make sure your application works on different
		/// aspect ratios
		/// </summary>
		FixedWidth,

		/// <summary>
		/// Pixel perfect version of FixedWidth. Scaling is limited to integer values.
		/// </summary>
		FixedWidthPixelPerfect,

		/// <summary>
		/// The application takes the width and height that best fits the design resolution with optional cropping inside of the "bleed area"
		/// and possible letter/pillar boxing. Works just like ShowAll except with horizontal/vertical bleed (padding). Gives you an area much
		/// like the old TitleSafeArea. Example: if design resolution is 1348x900 and bleed is 148x140 the safe area would be 1200x760 (design
		/// resolution - bleed).
		/// </summary>
		BestFit
	}

	public SceneData SceneData;

	/// <summary>
	/// default scene Camera
	/// </summary>
	public Camera Camera;

	/// <summary>
	/// clear color that is used in preRender to clear the screen
	/// </summary>
	public Color ClearColor = Color.CornflowerBlue;

	/// <summary>
	/// clear color for the final render of the RenderTarget to the framebuffer
	/// </summary>
	public Color LetterboxColor = Color.Black;

	/// <summary>
	/// SamplerState used for the final draw of the RenderTarget to the framebuffer
	/// </summary>
	public SamplerState SamplerState = Core.DefaultSamplerState;

	/// <summary>
	/// Scene-specific ContentManager. Use it to load up any resources that are needed only by this scene. If you have global/multi-scene
	/// resources you can use Core.contentManager to load them since Voltage will not ever unload them.
	/// </summary>
	public readonly VoltageContentManager Content;

	/// <summary>
	/// global toggle for PostProcessors
	/// </summary>
	public bool EnablePostProcessing = true;

	/// <summary>
	/// The list of entities within this Scene
	/// </summary>
	public readonly EntityList Entities;

	/// <summary>
	/// Manages a list of all the RenderableComponents that are currently on the Scene's Entities
	/// </summary>
	public readonly RenderableComponentList RenderableComponents;

	/// <summary>
	/// gets the size of the sceneRenderTarget
	/// </summary>
	/// <value>The size of the scene render texture.</value>
	public Point SceneRenderTargetSize =>
		new(_sceneRenderTarget.Bounds.Width, _sceneRenderTarget.Bounds.Height);

	/// <summary>
	/// accesses the main scene RenderTarget. Some Renderers that use multiple RenderTargets may need to render into them first and then
	/// render the result into the sceneRenderTarget.
	/// </summary>
	/// <value>The scene render target.</value>
	public RenderTarget2D SceneRenderTarget => _sceneRenderTarget;

	/// <summary>
	/// if the ResolutionPolicy is pixel perfect this will be set to the scale calculated for it
	/// </summary>
	public int PixelPerfectScale = 1;

	/// <summary>
	/// the final render to the screen can be deferred to this delegate if set. This is really only useful for cases where the final render
	/// might need a full screen size effect even though a small back buffer is used.
	/// </summary>
	/// <value>The final render delegate.</value>
	public IFinalRenderDelegate FinalRenderDelegate
	{
		set
		{
			_finalRenderDelegate?.Unload();
			_finalRenderDelegate = value;
			_finalRenderDelegate?.OnAddedToScene(this);
		}
		get => _finalRenderDelegate;
	}

	private IFinalRenderDelegate _finalRenderDelegate;


	#region SceneResolutionPolicy private fields

	/// <summary>
	/// default resolution size used for all scenes
	/// </summary>
	private static Point _defaultDesignResolutionSize;

	/// <summary>
	/// default bleed size for <see cref="SceneResolutionPolicy.BestFit"/> resolution policy
	/// </summary>
	private static Point _defaultDesignBleedSize;

	/// <summary>
	/// default resolution policy used for all scenes
	/// </summary>
	private static SceneResolutionPolicy _defaultSceneResolutionPolicy = SceneResolutionPolicy.None;

	/// <summary>
	/// resolution policy used by the scene
	/// </summary>
	private SceneResolutionPolicy _resolutionPolicy;

	/// <summary>
	/// design resolution size used by the scene
	/// </summary>
	private Point _designResolutionSize;

	/// <summary>
	/// bleed size for <see cref="SceneResolutionPolicy.BestFit"/> resolution policy
	/// </summary>
	private Point _designBleedSize;

	/// <summary>
	/// this gets setup based on the resolution policy and is used for the final blit of the RenderTarget
	/// </summary>
	private Rectangle _finalRenderDestinationRect;

	#endregion


	private RenderTarget2D _sceneRenderTarget;
	private RenderTarget2D _destinationRenderTarget;
	private Action<Texture2D> _screenshotRequestCallback;
	private readonly Dictionary<string, List<Delegate>> _entityAddedByNameCallbacks = new();
	private readonly Dictionary<int, List<Delegate>> _entityAddedByTagCallbacks = new();
	private readonly Dictionary<Type, List<Delegate>> _componentAddedCallbacks = new();

	internal readonly FastList<SceneComponent> _sceneComponents = new();
	internal readonly FastList<Renderer> _renderers = new();
	internal readonly FastList<Renderer> _afterPostProcessorRenderers = new();
	internal readonly FastList<PostProcessor> _postProcessors = new();
	private bool _didSceneBegin;


	/// <summary>
	/// sets the default design size and resolution policy that new scenes will use. horizontal/verticalBleed are only relevant for BestFit.
	/// </summary>
	/// <param name="width">Width.</param>
	/// <param name="height">Height.</param>
	/// <param name="sceneResolutionPolicy">Scene resolution policy.</param>
	/// <param name="horizontalBleed">Horizontal bleed size. Used only if resolution policy is set to <see cref="SceneResolutionPolicy.BestFit"/>.</param>
	/// <param name="verticalBleed">Vertical bleed size. Used only if resolution policy is set to <see cref="SceneResolutionPolicy.BestFit"/>.</param>
	public static void SetDefaultDesignResolution(int width, int height,
		SceneResolutionPolicy sceneResolutionPolicy,
		int horizontalBleed = 0, int verticalBleed = 0)
	{
		_defaultDesignResolutionSize = new Point(width, height);
		_defaultSceneResolutionPolicy = sceneResolutionPolicy;
		if (_defaultSceneResolutionPolicy == SceneResolutionPolicy.BestFit)
			_defaultDesignBleedSize = new Point(horizontalBleed, verticalBleed);
	}

	#region Events

	public static event Action OnSceneBegin;

	public void InvokeSceneBegin()
	{
		OnSceneBegin?.Invoke();
	}

	#endregion

	public Scene()
	{
		Entities = new EntityList(this);
		RenderableComponents = new RenderableComponentList();
		Content = new VoltageContentManager();

		var cameraEntity = SimpleCreateEntity<EntityData>("camera", Entity.InstanceType.NonSerialized);
		Camera = cameraEntity.AddComponent(new Camera());

		// setup our resolution policy. we'll commit it in begin
		_resolutionPolicy = _defaultSceneResolutionPolicy;
		_designResolutionSize = _defaultDesignResolutionSize;
		_designBleedSize = _defaultDesignBleedSize;
	}
	
	#region Scene lifecycle

	/// <summary>
	/// override this in Scene subclasses. this will be called when Core sets this scene as the active scene.
	/// </summary>
	public virtual void OnStart()
	{
	}

	public virtual void Begin()
	{
		// Load entities from SceneData AFTER the scene is set as active
		if (SceneData != null && SceneData.Entities != null && SceneData.Entities.Count > 0)
		{
			LoadSceneEntitiesData();
		}

		if (_renderers.Length == 0)
		{
			AddRenderer(new DefaultRenderer());
			Debug.Warn(
				"Scene has begun with no renderer. A DefaultRenderer was added automatically so that something is visible.");
		}

		Physics.Reset();

		// prep our render textures
		UpdateResolutionScaler();
		Core.GraphicsDevice.SetRenderTarget(_sceneRenderTarget);
		Core.Emitter.AddObserver(CoreEvents.GraphicsDeviceReset, OnGraphicsDeviceReset);
		Core.Emitter.AddObserver(CoreEvents.OrientationChanged, OnOrientationChanged);

		_didSceneBegin = true;
		OnStart();
		InvokeSceneBegin();
	}

	public virtual void End()
	{
		_didSceneBegin = false;

		// we kill Renderers and PostProcessors first since they rely on Entities
		for (var i = 0; i < _renderers.Length; i++)
			_renderers.Buffer[i].Unload();

		for (var i = 0; i < _postProcessors.Length; i++)
			_postProcessors.Buffer[i].Unload();

		// now we can remove the Entities and finally the SceneComponents
		Core.Emitter.RemoveObserver(CoreEvents.GraphicsDeviceReset, OnGraphicsDeviceReset);
		Core.Emitter.RemoveObserver(CoreEvents.OrientationChanged, OnOrientationChanged);
		Entities.RemoveAllEntities();

		for (var i = 0; i < _sceneComponents.Length; i++)
			_sceneComponents.Buffer[i].OnRemovedFromScene();
		_sceneComponents.Clear();

		Camera = null;
		Content.Dispose();
		_sceneRenderTarget.Dispose();
		Physics.Clear();

		if (_destinationRenderTarget != null)
			_destinationRenderTarget.Dispose();

		Unload();
	}

	/// <summary>
	/// override this in Scene subclasses and do any unloading necessary here. this is called when Core removes this scene from the active slot.
	/// </summary>
	public virtual void Unload()
	{
	}

	/// <summary>
	/// If in EditMode, will only update the Transform of entities
	/// </summary>
	public virtual void Update()
	{
		// we set the RenderTarget here so that the Viewport will match the RenderTarget properly
		Core.GraphicsDevice.SetRenderTarget(_sceneRenderTarget);

		// update our lists in case they have any changes
		Entities.UpdateLists();

		// update our SceneComponents
		for (var i = _sceneComponents.Length - 1; i >= 0; i--)
			if (_sceneComponents.Buffer[i].Enabled)
				_sceneComponents.Buffer[i].Update();

		// update our Entities
		Entities.Update();

		// we update our renderables after entity.update in case any new Renderables were added
		RenderableComponents.UpdateLists();
	}

	internal void Render()
	{
		if (_renderers.Length == 0)
		{
			Debug.Error("There are no Renderers in the Scene!");
			return;
		}

		// Renderers should always have those that require a RenderTarget first. They clear themselves and set themselves as
		// the current RenderTarget when they render. If the first Renderer wants the sceneRenderTarget we set and clear it now.
		if (_renderers[0].WantsToRenderToSceneRenderTarget)
		{
			Core.GraphicsDevice.SetRenderTarget(_sceneRenderTarget);
			Core.GraphicsDevice.Clear(ClearColor);
		}


		var lastRendererHadRenderTarget = false;
		for (var i = 0; i < _renderers.Length; i++)
		{
			// MonoGame follows the XNA implementation so it will clear the entire buffer if we change the render target even if null.
			// Because of that, we track when we are done with our RenderTargets and clear the scene at that time.
			if (lastRendererHadRenderTarget && _renderers.Buffer[i].WantsToRenderToSceneRenderTarget)
			{
				Core.GraphicsDevice.SetRenderTarget(_sceneRenderTarget);
				Core.GraphicsDevice.Clear(ClearColor);

				// force a Camera matrix update to account for the new Viewport size
				if (_renderers.Buffer[i].Camera != null)
					_renderers.Buffer[i].Camera.ForceMatrixUpdate();
				Camera.ForceMatrixUpdate();
			}

			_renderers.Buffer[i].Render(this);
			lastRendererHadRenderTarget = _renderers.Buffer[i].RenderTexture != null;
		}
	}

	/// <summary>
	/// any PostProcessors present get to do their processing then we do the final render of the RenderTarget to the screen.
	/// In almost all cases finalRenderTarget will be null. The only time it will have a value is the first frame of a
	/// SceneTransition if the transition is requesting the render.
	/// </summary>
	/// <returns>The render.</returns>
	internal void PostRender(RenderTarget2D finalRenderTarget = null)
	{
		var enabledCounter = 0;
		if (EnablePostProcessing)
			for (var i = 0; i < _postProcessors.Length; i++)
				if (_postProcessors.Buffer[i].Enabled)
				{
					var isEven = Mathf.IsEven(enabledCounter);
					enabledCounter++;

					var source = isEven ? _sceneRenderTarget : _destinationRenderTarget;
					var destination = !isEven ? _sceneRenderTarget : _destinationRenderTarget;
					_postProcessors.Buffer[i].Process(source, destination);
				}

		// deal with our Renderers that want to render after PostProcessors if we have any
		for (var i = 0; i < _afterPostProcessorRenderers.Length; i++)
		{
			if (i == 0)
			{
				// we need to set the proper RenderTarget here. We want the last one that was the destination of our PostProcessors
				var currentRenderTarget = Mathf.IsEven(enabledCounter) ? _sceneRenderTarget : _destinationRenderTarget;
				Core.GraphicsDevice.SetRenderTarget(currentRenderTarget);
			}

			// force a Camera matrix update to account for the new Viewport size
			if (_afterPostProcessorRenderers.Buffer[i].Camera != null)
				_afterPostProcessorRenderers.Buffer[i].Camera.ForceMatrixUpdate();
			_afterPostProcessorRenderers.Buffer[i].Render(this);
		}

		// if we have a screenshot request deal with it before the final render to the backbuffer
		if (_screenshotRequestCallback != null)
		{
			var tex = new Texture2D(Core.GraphicsDevice, _sceneRenderTarget.Width, _sceneRenderTarget.Height);
			var data = new int[tex.Bounds.Width * tex.Bounds.Height];

			var currentRenderTarget = Mathf.IsEven(enabledCounter) ? _sceneRenderTarget : _destinationRenderTarget;
			currentRenderTarget.GetData(data);
			tex.SetData(data);
			_screenshotRequestCallback(tex);

			_screenshotRequestCallback = null;
		}

		// render our final result to the backbuffer or let our delegate do so
		if (_finalRenderDelegate != null)
		{
			var currentRenderTarget = Mathf.IsEven(enabledCounter) ? _sceneRenderTarget : _destinationRenderTarget;
			_finalRenderDelegate.HandleFinalRender(finalRenderTarget, LetterboxColor, currentRenderTarget,
				_finalRenderDestinationRect, SamplerState);
		}
		else
		{
			var currentRenderTarget = Mathf.IsEven(enabledCounter) ? _sceneRenderTarget : _destinationRenderTarget;
			Core.GraphicsDevice.SetRenderTarget(finalRenderTarget);
			Core.GraphicsDevice.Clear(LetterboxColor);

			Graphics.Instance.Batcher.Begin(BlendState.Opaque, SamplerState, null, null);
			Graphics.Instance.Batcher.Draw(currentRenderTarget, _finalRenderDestinationRect, Color.White);
			Graphics.Instance.Batcher.End();
		}
	}

	private void OnGraphicsDeviceReset()
	{
		UpdateResolutionScaler();
	}

	private void OnOrientationChanged()
	{
		UpdateResolutionScaler();
	}

	#endregion
	
	#region Resolution Policy

	/// <summary>
	/// sets the design size and resolution policy then updates the render textures
	/// </summary>
	/// <param name="width">Width.</param>
	/// <param name="height">Height.</param>
	/// <param name="sceneResolutionPolicy">Scene resolution policy.</param>
	/// <param name="horizontalBleed">Horizontal bleed size. Used only if resolution policy is set to <see cref="SceneResolutionPolicy.BestFit"/>.</param>
	/// <param name="verticalBleed">Horizontal bleed size. Used only if resolution policy is set to <see cref="SceneResolutionPolicy.BestFit"/>.</param>
	public void SetDesignResolution(int width, int height, SceneResolutionPolicy sceneResolutionPolicy,
		int horizontalBleed = 0, int verticalBleed = 0)
	{
		_designResolutionSize = new Point(width, height);
		_resolutionPolicy = sceneResolutionPolicy;
		if (_resolutionPolicy == SceneResolutionPolicy.BestFit)
			_designBleedSize = new Point(horizontalBleed, verticalBleed);
		UpdateResolutionScaler();
	}

	private void UpdateResolutionScaler()
	{
		var designSize = _designResolutionSize;
		var screenSize = new Point(Screen.Width, Screen.Height);
		var screenAspectRatio = (float)screenSize.X / (float)screenSize.Y;

		var renderTargetWidth = screenSize.X;
		var renderTargetHeight = screenSize.Y;

		var resolutionScaleX = (float)screenSize.X / (float)designSize.X;
		var resolutionScaleY = (float)screenSize.Y / (float)designSize.Y;

		var rectCalculated = false;

		// calculate the scale used by the PixelPerfect variants
		PixelPerfectScale = 1;
		if (_resolutionPolicy != SceneResolutionPolicy.None)
		{
			if ((float)designSize.X / (float)designSize.Y > screenAspectRatio)
				PixelPerfectScale = screenSize.X / designSize.X;
			else
				PixelPerfectScale = screenSize.Y / designSize.Y;

			if (PixelPerfectScale == 0)
				PixelPerfectScale = 1;
		}

		switch (_resolutionPolicy)
		{
			case SceneResolutionPolicy.None:
				_finalRenderDestinationRect.X = _finalRenderDestinationRect.Y = 0;
				_finalRenderDestinationRect.Width = screenSize.X;
				_finalRenderDestinationRect.Height = screenSize.Y;
				rectCalculated = true;
				break;
			case SceneResolutionPolicy.ExactFit:
				// exact design size render texture
				renderTargetWidth = designSize.X;
				renderTargetHeight = designSize.Y;
				break;
			case SceneResolutionPolicy.NoBorder:
				// exact design size render texture
				renderTargetWidth = designSize.X;
				renderTargetHeight = designSize.Y;

				resolutionScaleX = resolutionScaleY = Math.Max(resolutionScaleX, resolutionScaleY);
				break;
			case SceneResolutionPolicy.NoBorderPixelPerfect:
				// exact design size render texture
				renderTargetWidth = designSize.X;
				renderTargetHeight = designSize.Y;

				// we are going to do some cropping so we need to use floats for the scale then round up
				PixelPerfectScale = 1;
				if ((float)designSize.X / (float)designSize.Y < screenAspectRatio)
				{
					var floatScale = (float)screenSize.X / (float)designSize.X;
					PixelPerfectScale = Mathf.CeilToInt(floatScale);
				}
				else
				{
					var floatScale = (float)screenSize.Y / (float)designSize.Y;
					PixelPerfectScale = Mathf.CeilToInt(floatScale);
				}

				if (PixelPerfectScale == 0)
					PixelPerfectScale = 1;

				_finalRenderDestinationRect.Width = Mathf.CeilToInt(designSize.X * PixelPerfectScale);
				_finalRenderDestinationRect.Height = Mathf.CeilToInt(designSize.Y * PixelPerfectScale);
				_finalRenderDestinationRect.X = (screenSize.X - _finalRenderDestinationRect.Width) / 2;
				_finalRenderDestinationRect.Y = (screenSize.Y - _finalRenderDestinationRect.Height) / 2;
				rectCalculated = true;

				break;
			case SceneResolutionPolicy.ShowAll:
				resolutionScaleX = resolutionScaleY = Math.Min(resolutionScaleX, resolutionScaleY);

				renderTargetWidth = designSize.X;
				renderTargetHeight = designSize.Y;
				break;
			case SceneResolutionPolicy.ShowAllPixelPerfect:
				// exact design size render texture
				renderTargetWidth = designSize.X;
				renderTargetHeight = designSize.Y;

				_finalRenderDestinationRect.Width = Mathf.CeilToInt(designSize.X * PixelPerfectScale);
				_finalRenderDestinationRect.Height = Mathf.CeilToInt(designSize.Y * PixelPerfectScale);
				_finalRenderDestinationRect.X = (screenSize.X - _finalRenderDestinationRect.Width) / 2;
				_finalRenderDestinationRect.Y = (screenSize.Y - _finalRenderDestinationRect.Height) / 2;
				rectCalculated = true;

				break;
			case SceneResolutionPolicy.FixedHeight:
				resolutionScaleX = resolutionScaleY;
				designSize.X = Mathf.CeilToInt(screenSize.X / resolutionScaleX);

				// exact design size render texture for height but not width
				renderTargetWidth = designSize.X;
				renderTargetHeight = designSize.Y;
				break;
			case SceneResolutionPolicy.FixedHeightPixelPerfect:
				// start with exact design size render texture height. the width may change
				renderTargetHeight = designSize.Y;

				_finalRenderDestinationRect.Width = Mathf.CeilToInt(designSize.X * resolutionScaleX);
				_finalRenderDestinationRect.Height = Mathf.CeilToInt(designSize.Y * PixelPerfectScale);
				_finalRenderDestinationRect.X = (screenSize.X - _finalRenderDestinationRect.Width) / 2;
				_finalRenderDestinationRect.Y = (screenSize.Y - _finalRenderDestinationRect.Height) / 2;
				rectCalculated = true;

				renderTargetWidth = (int)(designSize.X * resolutionScaleX / PixelPerfectScale);
				break;
			case SceneResolutionPolicy.FixedWidth:
				resolutionScaleY = resolutionScaleX;
				designSize.Y = Mathf.CeilToInt(screenSize.Y / resolutionScaleY);

				// exact design size render texture for width but not height
				renderTargetWidth = designSize.X;
				renderTargetHeight = designSize.Y;
				break;
			case SceneResolutionPolicy.FixedWidthPixelPerfect:
				// start with exact design size render texture width. the height may change
				renderTargetWidth = designSize.X;

				_finalRenderDestinationRect.Width = Mathf.CeilToInt(designSize.X * PixelPerfectScale);
				_finalRenderDestinationRect.Height = Mathf.CeilToInt(designSize.Y * resolutionScaleY);
				_finalRenderDestinationRect.X = (screenSize.X - _finalRenderDestinationRect.Width) / 2;
				_finalRenderDestinationRect.Y = (screenSize.Y - _finalRenderDestinationRect.Height) / 2;
				rectCalculated = true;

				renderTargetHeight = (int)(designSize.Y * resolutionScaleY / PixelPerfectScale);

				break;
			case SceneResolutionPolicy.BestFit:
				var safeScaleX = (float)screenSize.X / (designSize.X - _designBleedSize.X);
				var safeScaleY = (float)screenSize.Y / (designSize.Y - _designBleedSize.Y);

				var resolutionScale = MathHelper.Max(resolutionScaleX, safeScaleX);
				var safeScale = MathHelper.Min(safeScaleX, safeScaleY);

				resolutionScaleX = resolutionScaleY = MathHelper.Min(resolutionScale, safeScale);

				renderTargetWidth = designSize.X;
				renderTargetHeight = designSize.Y;

				break;
		}

		// if we didnt already calculate a rect (None and all pixel perfect variants calculate it themselves) calculate it now
		if (!rectCalculated)
		{
			// calculate the display rect of the RenderTarget
			var renderWidth = designSize.X * resolutionScaleX;
			var renderHeight = designSize.Y * resolutionScaleY;

			_finalRenderDestinationRect = RectangleExt.FromFloats((screenSize.X - renderWidth) / 2,
				(screenSize.Y - renderHeight) / 2, renderWidth, renderHeight);
		}


		// set some values in the Input class to translate mouse position to our scaled resolution
		var scaleX = renderTargetWidth / (float)_finalRenderDestinationRect.Width;
		var scaleY = renderTargetHeight / (float)_finalRenderDestinationRect.Height;

		Input._resolutionScale = new Vector2(scaleX, scaleY);
		Input._resolutionOffset = _finalRenderDestinationRect.Location;

		// resize our RenderTargets
		if (_sceneRenderTarget != null)
			_sceneRenderTarget.Dispose();
		_sceneRenderTarget = RenderTarget.Create(renderTargetWidth, renderTargetHeight);

		// only create the destinationRenderTarget if it already exists, which would indicate we have PostProcessors
		if (_destinationRenderTarget != null)
		{
			_destinationRenderTarget.Dispose();
			_destinationRenderTarget = RenderTarget.Create(renderTargetWidth, renderTargetHeight);
		}

		// notify the Renderers, PostProcessors and FinalRenderDelegate of the change in render texture size
		for (var i = 0; i < _renderers.Length; i++)
			_renderers.Buffer[i].OnSceneBackBufferSizeChanged(renderTargetWidth, renderTargetHeight);

		for (var i = 0; i < _afterPostProcessorRenderers.Length; i++)
			_afterPostProcessorRenderers.Buffer[i]
				.OnSceneBackBufferSizeChanged(renderTargetWidth, renderTargetHeight);

		for (var i = 0; i < _postProcessors.Length; i++)
			_postProcessors.Buffer[i].OnSceneBackBufferSizeChanged(renderTargetWidth, renderTargetHeight);

		if (_finalRenderDelegate != null)
			_finalRenderDelegate.OnSceneBackBufferSizeChanged(renderTargetWidth, renderTargetHeight);

		Camera.OnSceneRenderTargetSizeChanged(renderTargetWidth, renderTargetHeight);
	}

	#endregion
	
	#region Utils

	/// <summary>
	/// after the next draw completes this will clone the backbuffer and call callback with the clone. Note that you must dispose of the
	/// Texture2D when done with it!
	/// </summary>
	/// <param name="callback">Callback.</param>
	public void RequestScreenshot(Action<Texture2D> callback)
	{
		_screenshotRequestCallback = callback;
	}

	#endregion

	#region SceneComponent Management

	/// <summary>
	/// Adds and returns a SceneComponent to the components list
	/// </summary>
	/// <returns>Scene.</returns>
	/// <typeparam name="T">The 1st type parameter.</typeparam>
	public T AddSceneComponent<T>() where T : SceneComponent, new()
	{
		return AddSceneComponent(new T());
	}

	/// <summary>
	/// Adds and returns a SceneComponent to the components list
	/// </summary>
	/// <returns>Scene.</returns>
	/// <typeparam name="T">The 1st type parameter.</typeparam>
	public T AddSceneComponent<T>(T component) where T : SceneComponent
	{
		component.Scene = this;
		component.OnEnabled();
		_sceneComponents.Add(component);
		_sceneComponents.Sort();
		return component;
	}

	/// <summary>
	/// Gets the first SceneComponent of type T and returns it. If no component is found returns null.
	/// </summary>
	/// <returns>The component.</returns>
	/// <typeparam name="T">The 1st type parameter.</typeparam>
	public T GetSceneComponent<T>() where T : SceneComponent
	{
		for (var i = 0; i < _sceneComponents.Length; i++)
		{
			var component = _sceneComponents.Buffer[i];
			if (component is T)
				return component as T;
		}

		return null;
	}

	/// <summary>
	/// Gets the first SceneComponent of type T and returns it. If no SceneComponent is found the SceneComponent will be created.
	/// </summary>
	/// <returns>The component.</returns>
	/// <typeparam name="T">The 1st type parameter.</typeparam>
	public T GetOrCreateSceneComponent<T>() where T : SceneComponent, new()
	{
		var comp = GetSceneComponent<T>();
		if (comp == null)
			comp = AddSceneComponent<T>();

		return comp;
	}

	/// <summary>
	/// removes the first SceneComponent of type T from the components list
	/// </summary>
	/// <returns><c>true</c>, if component was removed, <c>false</c> otherwise.</returns>
	/// <typeparam name="T">The 1st type parameter.</typeparam>
	public bool RemoveSceneComponent<T>() where T : SceneComponent
	{
		var comp = GetSceneComponent<T>();
		if (comp != null)
		{
			RemoveSceneComponent(comp);
			return true;
		}

		return false;
	}

	/// <summary>
	/// removes a SceneComponent from the SceneComponents list
	/// </summary>
	public void RemoveSceneComponent(SceneComponent component)
	{
		Insist.IsTrue(_sceneComponents.Contains(component), "SceneComponent {0} is not in the SceneComponents list!",
			component);
		_sceneComponents.Remove(component);
		component.OnRemovedFromScene();
	}

	#endregion

	#region Renderer/PostProcessor Management

	/// <summary>
	/// adds a Renderer to the scene
	/// </summary>
	/// <returns>The renderer.</returns>
	/// <param name="renderer">Renderer.</param>
	public T AddRenderer<T>(T renderer) where T : Renderer
	{
		if (renderer.WantsToRenderAfterPostProcessors)
		{
			_afterPostProcessorRenderers.Add(renderer);
			_afterPostProcessorRenderers.Sort();
		}
		else
		{
			_renderers.Add(renderer);
			_renderers.Sort();
		}

		renderer.OnAddedToScene(this);

		// if we already began let the PostProcessor know what size our RenderTarget is
		if (_didSceneBegin)
			renderer.OnSceneBackBufferSizeChanged(_sceneRenderTarget.Width, _sceneRenderTarget.Height);

		return renderer;
	}

	/// <summary>
	/// gets the first Renderer of Type T
	/// </summary>
	/// <returns>The renderer.</returns>
	public T GetRenderer<T>() where T : Renderer
	{
		for (var i = 0; i < _renderers.Length; i++)
			if (_renderers.Buffer[i] is T)
				return _renderers[i] as T;

		for (var i = 0; i < _afterPostProcessorRenderers.Length; i++)
			if (_afterPostProcessorRenderers.Buffer[i] is T)
				return _afterPostProcessorRenderers.Buffer[i] as T;

		return null;
	}

	/// <summary>
	/// removes the Renderer from the scene
	/// </summary>
	/// <param name="renderer">Renderer.</param>
	public void RemoveRenderer(Renderer renderer)
	{
		Insist.IsTrue(_renderers.Contains(renderer) || _afterPostProcessorRenderers.Contains(renderer));

		if (renderer.WantsToRenderAfterPostProcessors)
			_afterPostProcessorRenderers.Remove(renderer);
		else
			_renderers.Remove(renderer);
		renderer.Unload();
	}

	/// <summary>
	/// adds a PostProcessor to the scene. Sets the scene field and calls PostProcessor.onAddedToScene so that PostProcessors can load
	/// resources using the scenes ContentManager.
	/// </summary>
	/// <param name="postProcessor">Post processor.</param>
	public T AddPostProcessor<T>(T postProcessor) where T : PostProcessor
	{
		_postProcessors.Add(postProcessor);
		_postProcessors.Sort();
		postProcessor.OnAddedToScene(this);

		// if we already began let the PostProcessor know what size our RenderTarget is
		if (_didSceneBegin)
			postProcessor.OnSceneBackBufferSizeChanged(_sceneRenderTarget.Width, _sceneRenderTarget.Height);

		// lazily create the 2nd RenderTarget for post processing only when a PostProcessor is added
		if (_destinationRenderTarget == null)
		{
			if (_sceneRenderTarget != null)
				_destinationRenderTarget = RenderTarget.Create(_sceneRenderTarget.Width, _sceneRenderTarget.Height);
			else
				_destinationRenderTarget = RenderTarget.Create();
		}

		return postProcessor;
	}

	/// <summary>
	/// gets the first PostProcessor of Type T
	/// </summary>
	/// <returns>The post processor.</returns>
	/// <typeparam name="T">The 1st type parameter.</typeparam>
	public T GetPostProcessor<T>() where T : PostProcessor
	{
		for (var i = 0; i < _postProcessors.Length; i++)
			if (_postProcessors.Buffer[i] is T)
				return _postProcessors[i] as T;

		return null;
	}

	/// <summary>
	/// removes a PostProcessor. Note that unload is not called when removing so if you no longer need the PostProcessor be sure to call
	/// unload to free resources.
	/// </summary>
	/// <param name="postProcessor">Step.</param>
	public void RemovePostProcessor(PostProcessor postProcessor)
	{
		Insist.IsTrue(_postProcessors.Contains(postProcessor));

		_postProcessors.Remove(postProcessor);
		postProcessor.Unload();
	}

	#endregion

	#region Events

	public static event Action OnFinishedAddingEntities;
	public static event Action<Entity> OnFinishedAddingEntitiesWithData;

	public static void InvokeFinishedAddingEntities()
	{
		OnFinishedAddingEntities?.Invoke();
	}

	public static void InvokeFinishedAddingEntitiesWithData(Entity entity)
	{
		OnFinishedAddingEntitiesWithData?.Invoke(entity);
	}

	#endregion

	#region Entity Management

	/// <summary>
	/// add the Entity to this Scene, and return it
	/// </summary>
	/// <returns></returns>
	public Entity SimpleCreateEntity<TData>(string name, Entity.InstanceType type) where TData : EntityData, new()
	{
		var entity = new Entity(name, type);
		if (entity.EntityData == null)
			entity.EntityData = new TData();

		return AddEntity(entity);
	}

	/// <summary>
	/// add the Entity to this Scene at position, and return it
	/// </summary>
	/// <returns>The entity.</returns>
	/// <param name="name">Name.</param>
	/// <param name="position">Position.</param>
	public Entity SimpleCreateEntity<TData>(string name, Vector2 position) where TData : EntityData, new()
	{
		var entity = new Entity(name);
		entity.Transform.Position = position;
		if (entity.EntityData == null)
			entity.EntityData = new TData();

		return AddEntity(entity);
	}

	public Entity SimpleCreateEntity(string name, Entity.InstanceType type)
	{
		var entity = new Entity(name, type);
		return AddEntity(entity);
	}

	#region Wait for Entity Added

	/// <summary>
	/// Registers a callback that will be invoked whenever an entity with the specified name is added to the scene.
	/// <para>
	/// This is useful for systems or scripts that need to automatically act on specific entities as they are added,
	/// such as setting up components, event hooks, or performing initialization logic.
	/// </para>
	/// </summary>
	/// <param name="entityName">The name of the entity to listen for.</param>
	/// <param name="onAdded">
	/// The function to execute when an entity with the specified name is added to the scene.
	/// The entity instance will be passed as the parameter.
	/// </param>
	public void OnEntityAddedByName(string entityName, Action<Entity> onAdded)
	{
		if (!_entityAddedByNameCallbacks.TryGetValue(entityName, out var list))
		{
			list = new List<Delegate>();
			_entityAddedByNameCallbacks[entityName] = list;
		}

		list.Add(onAdded);
	}

	/// <summary>
	/// Registers a callback that will be called **once** for the first entity with the specified name added to the scene,
	/// then the callback is automatically removed.
	/// </summary>
	/// <param name="entityName">The name of the entity to listen for.</param>
	/// <param name="onAdded">The callback to invoke when the entity is added.</param>
	public void OnEntityAddedByNameOnce(string entityName, Action<Entity> onAdded)
	{
		var oneShot = new OneShotDelegate<Entity>(onAdded);
		OnEntityAddedByName(entityName, oneShot.Invoke);
	}

	/// <summary>
	/// Registers a callback that will be invoked whenever an entity with the specified tag is added to the scene.
	/// <para>
	/// This is useful for systems or scripts that need to automatically act on entities with a specific tag,
	/// such as setting up components, event hooks, or performing initialization logic.
	/// </para>
	/// </summary>
	/// <param name="tag">The tag to listen for.</param>
	/// <param name="onAdded">
	/// The function to execute when an entity with the specified tag is added to the scene.
	/// The entity instance will be passed as the parameter.
	/// </param>
	public void OnEntityAddedByTag(int tag, Action<Entity> onAdded)
	{
		if (!_entityAddedByTagCallbacks.TryGetValue(tag, out var list))
		{
			list = new List<Delegate>();
			_entityAddedByTagCallbacks[tag] = list;
		}

		list.Add(onAdded);
	}

	/// <summary>
	/// Registers a callback that will be called **once** for the first entity with the specified tag added to the scene,
	/// then the callback is automatically removed.
	/// </summary>
	/// <param name="tag">The tag to listen for.</param>
	/// <param name="onAdded">The callback to invoke when the entity is added.</param>
	public void OnEntityAddedByTagOnce(int tag, Action<Entity> onAdded)
	{
		var oneShot = new OneShotDelegate<Entity>(onAdded);
		OnEntityAddedByTag(tag, oneShot.Invoke);
	}

	private void TriggerEntityAddedCallbacks(Entity entity)
	{
		var delegatesToRemove = new List<(string, Delegate)>();

		// Trigger callbacks registered by name
		if (_entityAddedByNameCallbacks.TryGetValue(entity.Name, out var nameCallbacks))
		{
			foreach (var del in nameCallbacks.ToArray()) // ToArray avoids modification during enumeration
			{
				del.DynamicInvoke(entity);

				// Remove if this is a one-shot delegate
				if (del.Target is IOneShotDelegate)
					delegatesToRemove.Add((entity.Name, del));
			}
		}

		// Remove one-shot delegates after invoking (by name)
		foreach (var (name, d) in delegatesToRemove)
			_entityAddedByNameCallbacks[name].Remove(d);

		delegatesToRemove.Clear();
		var tagDelegatesToRemove = new List<(int, Delegate)>();

		// Trigger callbacks registered by tag
		if (_entityAddedByTagCallbacks.TryGetValue(entity.Tag, out var tagCallbacks))
		{
			foreach (var del in tagCallbacks.ToArray())
			{
				del.DynamicInvoke(entity);

				// Remove if this is a one-shot delegate
				if (del.Target is IOneShotDelegate)
					tagDelegatesToRemove.Add((entity.Tag, del));
			}
		}

		// Remove one-shot delegates after invoking (by tag)
		foreach (var (tag, d) in tagDelegatesToRemove)
			_entityAddedByTagCallbacks[tag].Remove(d);
	}

	#endregion


	#region Wait For Component Added

	/// <summary>
	/// Registers a callback that will be invoked whenever a component of type <typeparamref name="T"/> is added to any entity in the scene.
	/// </summary>
	public void OnComponentAddedToScene<T>(Action<T> onAdded) where T : Component
	{
		var type = typeof(T);
		if (!_componentAddedCallbacks.TryGetValue(type, out var list))
		{
			list = new List<Delegate>();
			_componentAddedCallbacks[type] = list;
		}

		list.Add(onAdded);
	}

	/// <summary>
	/// Registers a callback that will be called **once** for the first component of type T added to the scene,
	/// then the callback is automatically removed.
	/// </summary>
	public void OnComponentAddedToSceneOnce<T>(Action<T> onAdded) where T : Component
	{
		var oneShot = new OneShotDelegate<T>(onAdded);
		OnComponentAddedToScene<T>(oneShot.Invoke);
	}

	internal void TriggerComponentAddedCallbacks(Component component)
	{
		var type = component.GetType();
		var delegatesToRemove = new List<(Type, Delegate)>();

		foreach (var kvp in _componentAddedCallbacks)
		{
			if (kvp.Key.IsAssignableFrom(type))
			{
				foreach (var del in kvp.Value.ToArray())
				{
					del.DynamicInvoke(component);

					// Remove if this is a one-shot delegate
					if (del.Target is IOneShotDelegate)
						delegatesToRemove.Add((kvp.Key, del));
				}
			}
		}

		// Remove one-shot delegates after invoking
		foreach (var (t, d) in delegatesToRemove)
			_componentAddedCallbacks[t].Remove(d);
	}

	#endregion

	/// <summary>
	/// adds an Entity to the Scene's Entities list
	/// </summary>
	/// <param name="entity">The Entity to add</param>
	public virtual Entity AddEntity(Entity entity)
	{
		if (Entities.FindEntity(entity.Name) != null)
			entity.Name = GetUniqueEntityName(entity.Name, entity);

		Entities.Add(entity);
		entity.Scene = this;

		// Recursively add child entities
		for (var i = 0; i < entity.Transform.ChildCount; i++)
		{
			var childEntity = entity.Transform.GetChild(i).Entity;
			AddEntity(childEntity);
		}

		TriggerEntityAddedCallbacks(entity);
		return entity;
	}

	/// <summary>
	/// removes all entities from the scene
	/// </summary>
	public void DestroyAllEntities()
	{
		for (var i = 0; i < Entities.Count; i++)
			Entities[i].Destroy();
	}

	/// <summary>
	/// searches for and returns the first Entity with name
	/// </summary>
	/// <returns>The entity.</returns>
	/// <param name="name">Name.</param>
	public Entity FindEntity(string name)
	{
		return Entities.FindEntity(name);
	}

	/// <summary>
	/// returns all entities with the given tag
	/// </summary>
	/// <returns>The entities by tag.</returns>
	/// <param name="tag">Tag.</param>
	public List<Entity> FindEntitiesWithTag(int tag)
	{
		return Entities.EntitiesWithTag(tag);
	}

	/// <summary>
	/// returns the first enabled loaded component of Type T
	/// </summary>
	/// <returns>The component of type.</returns>
	/// <typeparam name="T">The 1st type parameter.</typeparam>
	public T FindComponentOfType<T>() where T : Component
	{
		return Entities.FindComponentOfType<T>();
	}

	/// <summary>
	/// returns a list of all enabled loaded components of Type T
	/// </summary>
	/// <returns>The components of type.</returns>
	/// <typeparam name="T">The 1st type parameter.</typeparam>
	public List<T> FindComponentsOfType<T>() where T : Component
	{
		return Entities.FindComponentsOfType<T>();
	}

	/// <summary>
	/// returns the first enabled loaded component of Type T with the specified name
	/// </summary>
	/// <returns>The component with the given name and type.</returns>
	/// <param name="name">Name of the component to find.</param>
	/// <typeparam name="T">The component type.</typeparam>
	public T FindComponentWithName<T>(string name) where T : Component
	{
		return Entities.FindComponentWithName<T>(name);
	}

	/// <summary>
	/// Pattern: BaseName + optional separator + optional number at the end e.g. Platform, Platform_1, Platform-1, Platform1
	/// </summary>
	/// <param name="baseName"></param>
	/// <returns></returns>
	public string GetUniqueEntityName(string baseName, Entity entity, IEnumerable<Entity> pendingEntities = null)
	{
		var baseLower = baseName.ToLower();

		var allNames = new List<string>();
		for (var i = 0; i < Entities.Count; i++)
		{
			if (Entities[i] == entity)
				continue;
			allNames.Add(Entities[i].Name.ToLower());
		}

		if (pendingEntities != null)
		{
			foreach (var e in pendingEntities)
			{
				if (e == entity)
					continue;
				allNames.Add(e.Name.ToLower());
			}
		}

		if (!allNames.Contains(baseLower))
			return baseName;

		var inputPattern = @"^(.+?)[_\-]?(\d+)$";
		var inputMatch = System.Text.RegularExpressions.Regex.Match(baseName, inputPattern);

		string actualBaseName;
		int startingNumber;

		if (inputMatch.Success)
		{
			actualBaseName = inputMatch.Groups[1].Value;
			startingNumber = int.Parse(inputMatch.Groups[2].Value);
		}
		else
		{
			actualBaseName = baseName;
			startingNumber = 1;
		}

		var baseNameLower = actualBaseName.ToLower();
		var pattern = @"^" + System.Text.RegularExpressions.Regex.Escape(baseNameLower) + @"(?:[_\-]?(\d+))?$";
		var maxNum = startingNumber - 1;

		foreach (var name in allNames)
		{
			var match = System.Text.RegularExpressions.Regex.Match(name, pattern);
			if (match.Success)
			{
				if (string.IsNullOrEmpty(match.Groups[1].Value))
				{
					maxNum = Math.Max(maxNum, 0);
				}
				else if (int.TryParse(match.Groups[1].Value, out var num))
				{
					maxNum = Math.Max(maxNum, num);
				}
			}
		}

		return $"{actualBaseName}_{maxNum + 1}";
	}

	#endregion

	#region Scene Serialization

	/// <summary>
	/// Loads a scene from a .vscene file path
	/// </summary>
	public static Scene LoadFromFile(string scenePath)
	{
		if (string.IsNullOrWhiteSpace(scenePath))
		{
			Debug.Error("Scene path cannot be null or empty");
			return null;
		}

		if (!File.Exists(scenePath))
		{
			Debug.Error($"Scene file not found: {scenePath}");
			return null;
		}

		try
		{
			var jsonContent = File.ReadAllText(scenePath);
			var sceneData = Persistence.Json.FromJson<SceneData>(jsonContent);
			
			if (sceneData == null)
			{
				Debug.Error($"Failed to deserialize scene from: {scenePath}");
				return null;
			}

			var scene = new Scene();
			scene.ApplySceneData(sceneData);
			
			return scene;
		}
		catch (Exception ex)
		{
			Debug.Error($"Failed to load scene from '{scenePath}': {ex.Message}." +
			            $" \n Stack trace: {ex.StackTrace}");
			return null;
		}
	}

	/// <summary>
	/// Saves this scene to a JSON file
	/// </summary>
	public bool SaveToFile(string scenePath)
	{
		if (string.IsNullOrWhiteSpace(scenePath))
		{
			Debug.Error("Scene path cannot be null or empty");
			return false;
		}

		try
		{
			// Build scene data from current state
			var sceneData = BuildSceneData();
			sceneData.FilePath = scenePath;
			
			// Update modification time
			sceneData.ModifiedAt = DateTime.Now;
			
			// Serialize to JSON
			var jsonSettings = new Voltage.Persistence.JsonSettings
			{
				PrettyPrint = true,
				TypeNameHandling = Voltage.Persistence.TypeNameHandling.Auto,
				PreserveReferencesHandling = false
			};
			
			var jsonContent = Voltage.Persistence.Json.ToJson(sceneData, jsonSettings);
			
			// Ensure directory exists
			var directory = Path.GetDirectoryName(scenePath);
			if (!string.IsNullOrEmpty(directory))
			{
				Directory.CreateDirectory(directory);
			}
			
			// Write to file
			File.WriteAllText(scenePath, jsonContent, new System.Text.UTF8Encoding(false));
			
			Debug.Log($"Successfully saved scene to: {scenePath}");
			return true;
		}
		catch (Exception ex)
		{
			Debug.Error($"Failed to save scene to '{scenePath}': {ex.Message}");
			Debug.Error($"Stack trace: {ex.StackTrace}");
			return false;
		}
	}

	/// <summary>
	/// Builds SceneData from the current scene state
	/// </summary>
	public SceneData BuildSceneData()
	{
		var sceneData = new SceneData();
		
		// Copy scene metadata
		if (SceneData != null)
		{
			sceneData.Name = SceneData.Name;
			sceneData.CreatedAt = SceneData.CreatedAt;
			sceneData.TiledMapFileName = SceneData.TiledMapFileName;
			sceneData.EditorData = SceneData.EditorData != null 
				? new Dictionary<string, string>(SceneData.EditorData) 
				: new Dictionary<string, string>();
		}
		else
		{
			// Fallback: use the scene type name if no SceneData exists
			sceneData.Name = GetType().Name;
		}
		
		// IMPORTANT: Ensure the name is never empty
		if (string.IsNullOrWhiteSpace(sceneData.Name))
		{
			sceneData.Name = "Untitled Scene";
		}
		
		// Copy scene settings
		sceneData.ClearColor = ClearColor;
		sceneData.LetterboxColor = LetterboxColor;
		sceneData.ResolutionPolicy = _resolutionPolicy.ToString();
		sceneData.DesignResolutionWidth = _designResolutionSize.X;
		sceneData.DesignResolutionHeight = _designResolutionSize.Y;
		sceneData.HorizontalBleed = _designBleedSize.X;
		sceneData.VerticalBleed = _designBleedSize.Y;
		sceneData.EnablePostProcessing = EnablePostProcessing;
		
		// Build entity data
		sceneData.Entities.Clear();
		
		foreach (var entity in Entities)
		{
			// Skip non-serialized entities (like the camera)
			if (entity.Type == Entity.InstanceType.NonSerialized)
				continue;

			var entityData = BuildEntityData(entity);
			sceneData.Entities.Add(entityData);
		}
		
		return sceneData;
	}

	/// <summary>
	/// Builds entity data from an entity
	/// </summary>
	private SceneData.SceneEntityData BuildEntityData(Entity entity)
	{
		Vector2 positionToSave;
		float rotationToSave;
		Vector2 scaleToSave;
		
		if (entity.Transform.Parent != null)
		{
			// Entity has a parent - save LOCAL transform values
			positionToSave = entity.Transform.LocalPosition;
			rotationToSave = entity.Transform.LocalRotation;
			scaleToSave = entity.Transform.LocalScale;
		}
		else
		{
			// Entity has no parent - save WORLD transform values
			positionToSave = entity.Transform.Position;
			rotationToSave = entity.Transform.Rotation;
			scaleToSave = entity.Transform.Scale;
		}

		var existingId = Guid.Empty;
		if (SceneData?.Entities != null)
			existingId = SceneData.Entities.FirstOrDefault(e => string.Equals(e.Name, entity.Name, StringComparison.OrdinalIgnoreCase))?.Id ?? Guid.Empty;

		Guid? parentId = null;
		var parentName = entity.Transform.Parent?.Entity?.Name;
		if (SceneData?.Entities != null && !string.IsNullOrWhiteSpace(parentName))
			parentId = SceneData.Entities.FirstOrDefault(e => string.Equals(e.Name, parentName, StringComparison.OrdinalIgnoreCase))?.Id;

		var entityData = new SceneData.SceneEntityData
		{
			Id = existingId != Guid.Empty ? existingId : Guid.NewGuid(),
			ParentId = parentId,
			InstanceType = entity.Type,
			Name = entity.Name,
			Position = positionToSave,
			Rotation = rotationToSave,
			Scale = scaleToSave,
			ParentEntityName = parentName,
			Enabled = entity.Enabled,
			UpdateOrder = entity.UpdateOrder,
			Tag = entity.Tag,
			IsSelectableInEditor = entity.IsSelectableInEditor,
			DebugRenderEnabled = entity.DebugRenderEnabled,
			OriginalPrefabName = entity.OriginalPrefabName
		};

		// Get entity-specific data
		var entData = entity.GetEntityData();
		if (entData != null)
		{
			entData.ComponentDataList.Clear();
			
			// Serialize all components
			foreach (var component in entity.Components)
			{
				if (component.Data != null)
				{
					var componentJsonSettings = new Voltage.Persistence.JsonSettings
					{
						PrettyPrint = true,
						TypeNameHandling = Voltage.Persistence.TypeNameHandling.Auto,
						PreserveReferencesHandling = false
					};
					
					var json = Voltage.Persistence.Json.ToJson(component.Data, componentJsonSettings);
					entData.ComponentDataList.Add(new ComponentDataEntry
					{
						ComponentTypeName = component.GetType().FullName,
						ComponentName = component.Name,
						DataTypeName = component.Data.GetType().FullName,
						Json = json
					});
				}
			}
			
			entityData.EntityData = entData;
		}

		return entityData;
	}

	/// <summary>
	/// Applies loaded SceneData to this scene
	/// </summary>
	private void ApplySceneData(SceneData sceneData)
	{
		if (sceneData == null)
		{
			Debug.Error("Cannot apply null SceneData");
			return;
		}
		
		SceneData = sceneData;
		
		ClearColor = sceneData.ClearColor;
		LetterboxColor = sceneData.LetterboxColor;
		EnablePostProcessing = sceneData.EnablePostProcessing;
		
		// Parse and apply resolution policy
		if (Enum.TryParse<SceneResolutionPolicy>(sceneData.ResolutionPolicy, out var resolutionPolicy))
		{
			SetDesignResolution(
				sceneData.DesignResolutionWidth,
				sceneData.DesignResolutionHeight,
				resolutionPolicy,
				sceneData.HorizontalBleed,
				sceneData.VerticalBleed
			);
		}
		else
		{
			Debug.Warn($"Unknown resolution policy: {sceneData.ResolutionPolicy}, using default");
			SetDesignResolution(1920, 1080, SceneResolutionPolicy.BestFit);
		}
	}

	/// <summary>
	/// Loads entities from the stored SceneData.
	/// This is called automatically during scene initialization.
	/// </summary>
	private void LoadSceneEntitiesData()
	{
		if (SceneData == null || SceneData.Entities == null)
		{
			Debug.Warn("No SceneData or entities to load");
			return;
		}

		var sceneEntitiesByName = new Dictionary<string, SceneData.SceneEntityData>(StringComparer.OrdinalIgnoreCase);
		var sceneEntitiesById = new Dictionary<Guid, SceneData.SceneEntityData>();
		
		for (var i = 0; i < SceneData.Entities.Count; i++)
		{
			var e = SceneData.Entities[i];
			if (!string.IsNullOrWhiteSpace(e.Name))
				sceneEntitiesByName[e.Name] = e;
			if (e.Id != Guid.Empty)
				sceneEntitiesById[e.Id] = e;
		}

		var entitiesNeedingParents = new List<Entity>();

		// NonSerialized entities (already in the scene, like camera)
		for (var i = 0; i < Entities.Count; i++)
		{
			if (Entities[i].Type != Entity.InstanceType.NonSerialized)
				continue;

			if (sceneEntitiesByName.TryGetValue(Entities[i].Name, out var sceneEntityData))
			{
				LoadEntityData(Entities[i], sceneEntityData);
				
				// Check if this entity needs parent assignment later
				if (!string.IsNullOrEmpty(Entities[i].GetData<string>("_PendingParentName")))
					entitiesNeedingParents.Add(Entities[i]);
			}
		}

		// Serialized & SerializedPrefab entities (to be created now)
		foreach (var sceneEntity in SceneData.Entities)
		{
			if (sceneEntity.InstanceType == Entity.InstanceType.NonSerialized)
				continue;

			var entity = new Entity(sceneEntity.Name);
			entity.Type = sceneEntity.InstanceType;
			AddEntity(entity);
			LoadEntityData(entity, sceneEntity);

			if (!string.IsNullOrEmpty(entity.GetData<string>("_PendingParentName")))
				entitiesNeedingParents.Add(entity);
		}

		AssignParentRelationships(entitiesNeedingParents);
		Debug.Info($"Loaded {SceneData.Entities.Count} entities from scene data");
	}

	/// <summary>
	/// Loads entity data into an entity instance
	/// </summary>
	private void LoadEntityData(Entity entity, SceneData.SceneEntityData entityData)
	{
		entity.Name = entityData.Name;
		entity.SetTag(entityData.Tag);
		entity.Enabled = entityData.Enabled;
		entity.UpdateOrder = entityData.UpdateOrder;
		entity.DebugRenderEnabled = entityData.DebugRenderEnabled;
		entity.Type = entityData.InstanceType;
		entity.IsSelectableInEditor = entityData.IsSelectableInEditor;

		if (entity.Type == Entity.InstanceType.SerializedPrefab)
			entity.OriginalPrefabName = entityData.OriginalPrefabName;
		else
			entity.OriginalPrefabName = null;

		// Handle transform and parent assignment (prefer ParentId if present)
		Entity parentEntity = null;
		if (entityData.ParentId.HasValue && SceneData?.Entities != null)
		{
			var parentSceneData = SceneData.Entities.FirstOrDefault(e => e.Id == entityData.ParentId.Value);
			if (parentSceneData != null)
				parentEntity = FindEntity(parentSceneData.Name);
		}
		if (parentEntity == null && !string.IsNullOrEmpty(entityData.ParentEntityName))
			parentEntity = FindEntity(entityData.ParentEntityName);

		if (parentEntity != null)
		{
			entity.Transform.SetParent(parentEntity.Transform);
			entity.Transform.SetLocalPosition(entityData.Position);
			entity.Transform.SetLocalRotation(entityData.Rotation);
			entity.Transform.SetLocalScale(entityData.Scale);
		}
		else
		{
			// Parent not found yet, save for later
			if (entityData.ParentId.HasValue)
				entity.SetData("_PendingParentId", entityData.ParentId.Value);
			else if (!string.IsNullOrEmpty(entityData.ParentEntityName))
				entity.SetData("_PendingParentName", entityData.ParentEntityName);

			entity.SetData("_PendingLocalPosition", (Vector2)entityData.Position);
			entity.SetData("_PendingLocalRotation", entityData.Rotation);
			entity.SetData("_PendingLocalScale", (Vector2)entityData.Scale);

			entity.Transform.Position = entityData.Position;
			entity.Transform.Rotation = entityData.Rotation;
			entity.Transform.Scale = entityData.Scale;
		}

		// Load entity-specific data and components
		if (entityData.EntityData != null)
		{
			var entDataType = entityData.EntityData.GetType();
			var json = Voltage.Persistence.Json.ToJson(entityData.EntityData, true);
			var deserializedEntityData = (EntityData)Voltage.Persistence.Json.FromJson(json, entDataType);

			// Deep clone ComponentDataList to avoid shared references
			if (deserializedEntityData.ComponentDataList != null)
			{
				deserializedEntityData.ComponentDataList = deserializedEntityData.ComponentDataList
					.Select(entry =>
					{
						var cloneJson = Voltage.Persistence.Json.ToJson(entry, true);
						return Voltage.Persistence.Json.FromJson<ComponentDataEntry>(cloneJson);
					})
					.ToList();
			}

			entity.SetEntityData(deserializedEntityData);

			// Instantiate components from ComponentDataList
			if (deserializedEntityData.ComponentDataList != null)
			{
				foreach (var componentEntry in deserializedEntityData.ComponentDataList)
				{
					try
					{
						// Get the component type from the type name
						var componentType = Type.GetType(componentEntry.ComponentTypeName);
						if (componentType == null)
						{
							Debug.Error($"Could not find component type: {componentEntry.ComponentTypeName}");
							continue;
						}

						// Create a new instance of the component
						var component = (Component)Activator.CreateInstance(componentType);
						component.Name = componentEntry.ComponentName;
						component.SetSerialized(true);

						// Add the component to the entity
						entity.AddComponent(component, true);
					}
					catch (Exception ex)
					{
						Debug.Error($"Failed to instantiate component {componentEntry.ComponentTypeName}: {ex.Message}");
					}
				}
			}

		var processedComponents = new HashSet<string>();

		// Assign data to already existing components (including newly added ones)
		foreach (var comp in entity.ComponentsToAdd)
		{
			if (TryAssignComponentData(entity, comp))
			{
				var componentId = $"{comp.GetType().FullName}:{comp.Name}";
				processedComponents.Add(componentId);
			}
		}

		// Register callback for components added later (if any are added dynamically)
		entity.OnComponentAdded<Component>(comp =>
		{
			var componentId = $"{comp.GetType().FullName}:{comp.Name}";
			
			if (!processedComponents.Contains(componentId))
			{
				TryAssignComponentData(entity, comp);
			}
		});
	}
	}

	/// <summary>
	/// Tries to assign component data from entity data
	/// </summary>
	private bool TryAssignComponentData(Entity entity, Component component)
	{
		ComponentDataSerializationBootstrap.EnsureInitialized();
		var entityData = entity.GetEntityData();

		if (entityData == null || entityData.ComponentDataList == null)
			return false;

		for (int i = entityData.ComponentDataList.Count - 1; i >= 0; i--)
		{
			var entry = entityData.ComponentDataList[i];

			if (component.Name == entry.ComponentName)
			{
				Type dataType = null;
				if (!string.IsNullOrWhiteSpace(entry.DataTypeName))
					dataType = Type.GetType(entry.DataTypeName);

				if (dataType != null)
				{
					try
					{
						var data = (ComponentData)Voltage.Persistence.Json.FromJson(entry.Json, dataType);
						component.Data = data;

						// Remove the processed entry
						entityData.ComponentDataList.RemoveAt(i);

						// Update the entity's data
						entity.SetEntityData(entityData);

						return true;
					}
					catch (Exception ex)
					{
						Debug.Error($"Error loading component data for {component.Name}: {ex.Message}");
						return false;
					}
				}
				else
				{
					Debug.Error($"Component type '{entry.DataTypeName}' is not registered in ComponentDataTypeRegistrator.DataTypes");
					return false;
				}
			}
		}

		return false;
	}

	/// <summary>
	/// Assigns parent relationships after all entities are loaded
	/// </summary>
	private void AssignParentRelationships(List<Entity> entitiesNeedingParents)
	{
		foreach (var entity in entitiesNeedingParents)
		{
			Entity parentEntity = null;
			var parentId = entity.GetData<Guid>("_PendingParentId");
			if (parentId != Guid.Empty && SceneData?.Entities != null)
			{
				var parentSceneData = SceneData.Entities.FirstOrDefault(e => e.Id == parentId);
				if (parentSceneData != null)
					parentEntity = FindEntity(parentSceneData.Name);
			}

			if (parentEntity == null)
			{
				var parentName = entity.GetData<string>("_PendingParentName");
				if (!string.IsNullOrEmpty(parentName))
					parentEntity = FindEntity(parentName);
			}

			if (parentEntity != null)
			{
				var savedLocalPosition = entity.GetData<Vector2>("_PendingLocalPosition");
				var savedLocalRotation = entity.GetData<float>("_PendingLocalRotation");
				var savedLocalScale = entity.GetData<Vector2>("_PendingLocalScale");

				entity.Transform.SetParent(parentEntity.Transform);
				entity.Transform.SetLocalPosition(savedLocalPosition);
				entity.Transform.SetLocalRotation(savedLocalRotation);
				entity.Transform.SetLocalScale(savedLocalScale);
			}
			else
			{
				Debug.Error($"Could not find parent entity for entity '{entity.Name}'");
			}

			// Clean up temporary data
			entity.RemoveData("_PendingParentId");
			entity.RemoveData("_PendingParentName");
			entity.RemoveData("_PendingLocalPosition");
			entity.RemoveData("_PendingLocalRotation");
			entity.RemoveData("_PendingLocalScale");
		}
	}

	#endregion
}