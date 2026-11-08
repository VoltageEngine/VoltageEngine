namespace Voltage.DeferredLighting
{
	public abstract class DeferredLight : RenderableComponent
	{
		/// <summary>
		/// we dont render lights normally so this method will do nothing and never be called. The DeferredLightingRenderer takes care of
		/// light rendering so that it can cache and reuse the light meshes.
		/// </summary>
		/// <param name="batcher">Batcher.</param>
		/// <param name="camera">Camera.</param>
		public override void Render(Batcher batcher, Camera camera)
		{
		}

		/// <summary>Visible everywhere by default; override it to cull a custom light. Built-in lights override it.</summary>
		public override RectangleF Bounds => Entity?.Scene != null ? Entity.Scene.Camera.Bounds : new RectangleF();

		/// <summary>Draws a custom light into the light buffer and returns true. Returning false leaves the light to the renderer's built-in types.</summary>
		public virtual bool RenderCustom(DeferredLightContext context) => false;
	}
}