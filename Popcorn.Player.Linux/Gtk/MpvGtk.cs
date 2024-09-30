using Gtk;
using Nickvision.MPVSharp;
using Nickvision.MPVSharp.Internal;
using Popcorn.Player.Linux.Player;

namespace Popcorn.Player.Linux.Gtk;

public static class MpvGtk
{
    private static RenderContext? _ctx;
    public static GLArea? GlArea { get; private set; }
    
    public static GLArea CreateGlArea()
    {
        GlArea = GLArea.New();
        GlArea.AutoRender = false;
        
        GlArea.SetVexpand(true);
        GlArea.SetHexpand(true);
        
        // Setup rendering
        GlArea.OnRealize += OnRealizeGLArea;
        GlArea.OnUnrealize += OnUnrealizeGLArea;
        GlArea.OnRender += OnRenderGLArea;
        return GlArea;
    }
    
    /// <summary>
    /// Occurs on GLArea initialization. Set up render context and start playing.
    /// </summary>
    /// <param name="sender">Gtk.Widget that invoked the event</param>
    /// <param name="e">EventArgs</param>
    private static void OnRealizeGLArea(Widget sender, EventArgs e)
    {
        GlArea!.MakeCurrent();
        var player = (MpvPlayer)Program.GetPlayer();
        player.Initialize();

        // Render offscreen, then transfer to GLArea
        _ctx = player.Client.CreateRenderContext();

        // We create callback for MPV to call every time the screen needs to be redrawn,
        // the callback calls GtkGLArea.QueueDraw which in turn makes RenderContext draw image
        _ctx.SetupGL((x) => GLib.Functions.IdleAdd(0, () =>
        {
            GlArea.QueueRender(); // QueueDraw must be called from GTK thread
            return false;
        }));
    }

    /// <summary>
    /// Occurs on GLArea redraw. Call RenderContext to redraw image
    /// </summary>
    /// <param name="sender">Gtk.Widget that invoked the event</param>
    /// <param name="e">EventArgs</param>
    private static bool OnRenderGLArea(Widget sender, EventArgs e)
    {
        GlArea!.MakeCurrent();
        var scaleFactor = Program.GetScaleFactor();
        _ctx?.RenderGL(GlArea.GetAllocatedWidth() * scaleFactor, GlArea.GetAllocatedHeight() * scaleFactor);
        return false;
    }
    
    /// <summary>
    /// Occurs on GLArea destruction. Free MPV objects
    /// </summary>
    /// <param name="sender">Gtk.Widget that invoked the event</param>
    /// <param name="e">EventArgs</param>
    private static void OnUnrealizeGLArea(Widget sender, EventArgs e)
    {
        _ctx?.Dispose();
        Program.GetPlayer().Dispose();
    }
}