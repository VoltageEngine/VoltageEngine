using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Voltage.Editor.DebugUtils;

namespace Voltage.Editor.Assets
{
    /// <summary>
    /// Captures SDL2 SDL_DROPFILE events so files dragged from the OS file manager
    /// into the editor window can be processed.
    ///
    /// Design:
    ///   • <see cref="SDL_AddEventWatch"/> installs a native callback that fires on the
    ///     SDL pump thread (MonoGame calls SDL_PumpEvents inside its main loop before
    ///     dispatching events).  We do NO editor-state mutation there — we only enqueue
    ///     the marshalled path string into a lock-free <see cref="ConcurrentQueue{T}"/>.
    ///   • <see cref="DrainPending"/> must be called from the main (ImGui) thread every
    ///     frame.  It dequeues all paths accumulated since the last call and invokes the
    ///     caller-supplied action with each one.
    ///   • The delegate passed to <see cref="SDL_AddEventWatch"/> is pinned for the
    ///     lifetime of this object via a <see cref="GCHandle"/> so the GC cannot collect
    ///     it while the native watch is active.
    ///   • <see cref="Dispose"/> removes the watch and frees the GC handle.
    /// </summary>
    internal sealed class SdlFileDropWatcher : IDisposable
    {
        // Native imports — SDL2.dll is a MonoGame.DesktopGL dependency; the platform config
        // ensures the correct native lib loads on Windows/macOS/Linux.

        // Callback type accepted by SDL_AddEventWatch / SDL_DelEventWatch.
        // Returns int (ignored by SDL for event watches), receives a userdata IntPtr
        // plus a pointer to the SDL_Event union.
        private delegate int SDL_EventFilter(IntPtr userdata, IntPtr sdlEvent);

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_AddEventWatch(SDL_EventFilter filter, IntPtr userdata);

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_DelEventWatch(SDL_EventFilter filter, IntPtr userdata);

        /// <summary>Free a pointer allocated by SDL (used after reading drop.file).</summary>
        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_free(IntPtr mem);

        // SDL_Event union is 56 bytes on all supported platforms.
        // We only need the first 4 bytes (type) and the SDL_DropEvent sub-struct:
        //   Uint32 type      — offset 0
        //   Uint32 timestamp — offset 4
        //   char*  file      — offset 8  (IntPtr-sized)
        //   Uint32 windowID  — offset 8 + sizeof(IntPtr)
        // Type and file pointer are accessed via unsafe pointer arithmetic.

        private const uint SDL_DROPFILE     = 0x1000;
        private const uint SDL_DROPBEGIN    = 0x1001;
        private const uint SDL_DROPCOMPLETE = 0x1002;

        private readonly ConcurrentQueue<string> _pendingPaths = new();
        private readonly SDL_EventFilter _filterDelegate;
        private readonly GCHandle _filterHandle; // keeps the delegate alive across GC cycles
        private bool _disposed;

        public SdlFileDropWatcher()
        {
            _filterDelegate = OnSdlEvent;
            // Prevent the GC from collecting the delegate while the native watch is active.
            _filterHandle   = GCHandle.Alloc(_filterDelegate);
            SDL_AddEventWatch(_filterDelegate, IntPtr.Zero);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            SDL_DelEventWatch(_filterDelegate, IntPtr.Zero);
            _filterHandle.Free();
        }

        /// <summary>
        /// Dequeues all file paths accumulated since the last call and passes each to
        /// <paramref name="onFilePath"/>.  Must be called from the main thread.
        /// </summary>
        public void DrainPending(Action<string> onFilePath)
        {
            while (_pendingPaths.TryDequeue(out var path))
            {
                try
                {
                    onFilePath(path);
                }
                catch (Exception ex)
                {
                    EditorDebug.Log($"SdlFileDropWatcher: error in drop handler: {ex.Message}", "AssetBrowser");
                }
            }
        }

        // Callback fires on the SDL pump thread — only enqueue, no editor-state mutation.
        private unsafe int OnSdlEvent(IntPtr userdata, IntPtr sdlEvent)
        {
            if (sdlEvent == IntPtr.Zero)
                return 0;

            uint evtType = *(uint*)sdlEvent;

            if (evtType == SDL_DROPFILE)
            {
                // SDL_DropEvent:
                //   uint32  type       — bytes 0..3
                //   uint32  timestamp  — bytes 4..7
                //   char*   file       — bytes 8..(8+ptrSize-1)
                var filePtr = *(IntPtr*)((byte*)sdlEvent + 8);
                if (filePtr != IntPtr.Zero)
                {
                    string path = Marshal.PtrToStringUTF8(filePtr);
                    SDL_free(filePtr);

                    if (!string.IsNullOrEmpty(path))
                        _pendingPaths.Enqueue(path);
                }
            }

            return 0; // return value is ignored by SDL for event watches
        }
    }
}
