using System;
using System.Runtime.InteropServices;

namespace Voltage.Editor.Utils
{
	/// <summary>
	/// The OS text clipboard, via SDL. Used so an entity copy can be pasted into a SECOND editor instance, which
	/// an in-process clipboard cannot do.
	/// </summary>
	public static class SdlClipboard
	{
		private delegate int SetClipboardTextDelegate(IntPtr utf8Text);
		private delegate IntPtr GetClipboardTextDelegate();
		private delegate void FreeDelegate(IntPtr memory);

		public static bool TrySetText(string text)
		{
			if (text == null || !SdlNative.TryGet<SetClipboardTextDelegate>("SDL_SetClipboardText", out var set))
				return false;

			var utf8 = Marshal.StringToCoTaskMemUTF8(text);

			try
			{
				return set(utf8) == 0;
			}
			catch
			{
				return false;
			}
			finally
			{
				Marshal.FreeCoTaskMem(utf8);
			}
		}

		public static bool TryGetText(out string text)
		{
			text = null;

			if (!SdlNative.TryGet<GetClipboardTextDelegate>("SDL_GetClipboardText", out var get))
				return false;

			var pointer = IntPtr.Zero;

			try
			{
				pointer = get();
				if (pointer == IntPtr.Zero)
					return false;

				text = Marshal.PtrToStringUTF8(pointer);
				return !string.IsNullOrEmpty(text);
			}
			catch
			{
				return false;
			}
			finally
			{
				// SDL allocates the returned string and hands ownership over.
				if (pointer != IntPtr.Zero && SdlNative.TryGet<FreeDelegate>("SDL_free", out var free))
					free(pointer);
			}
		}
	}
}
