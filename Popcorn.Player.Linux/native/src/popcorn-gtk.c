#include <mpv/client.h>
#include <mpv/render_gl.h>
#include <gio/gio.h>
#include <gtk/gtk.h>
#include <gtk/gtkglarea.h>
#include <stdio.h>
#include <locale.h>
#include <unistd.h>
#include <glib-object.h>
#include <glib/gi18n.h>
#include <gdk/gdk.h>
#include <stdlib.h>
#include <string.h>

#include <pthread.h>
#include <unistd.h>
#include <stdio.h>
#include <stdlib.h>

#include <epoxy/gl.h>
#ifdef GDK_WINDOWING_X11
#include <gdk/x11/gdkx.h>
#include <epoxy/glx.h>
#endif
#ifdef GDK_WINDOWING_WAYLAND
#include <gdk/wayland/gdkwayland.h>
#include <epoxy/egl.h>
#endif
#ifdef GDK_WINDOWING_WIN32
#include <gdk/win32/gdkwin32.h>
#include <epoxy/wgl.h>
#endif

#include <webkit/webkit.h>
#include <stddef.h>

#include <gtk/gtkwidget.h>
#include <gtk/gtkglarea.h>
#include "app_data.h"
#include "utils.h"

static void check_error(int status);
static void *get_proc_address(void *fn_ctx, const gchar *name);
static void render_frame(AppData *app_data);
static void render_update_callback(void *data);
static void init_mpv_render_context(AppData *app_data);
static void on_realize(GtkGLArea *area, gpointer user_data);
static void on_unrealize(GtkGLArea *area, gpointer user_data);
void init_mpv(AppData *app_data);
static gboolean emit_frame_ready(gpointer data);

static void render_update_callback(void *data)
{

    printf("render_update_callback, queuing frame render\n");
    AppData *app_data = (AppData *)data;
    if (app_data->gl_area == NULL)
    {
        printf("GLArea became NULL before rendering! - render_update_callback\n");
    }

    // Call the frame renderer in the main loop
    g_idle_add_full(G_PRIORITY_HIGH,
                    emit_frame_ready,
                    app_data,
                    NULL);
}

static gboolean emit_frame_ready(gpointer data)
{
    printf("\n");

    AppData *app_data = (AppData *)data;

    /**
     * The API user is supposed to call this when the update callback was invoked
     * (like all mpv_render_* functions, this has to happen on the render thread,
     * and _not_ from the update callback itself).
     *
     * This is optional if MPV_RENDER_PARAM_ADVANCED_CONTROL was not set (default).
     * Otherwise, it's a hard requirement that this is called after each update
     * callback. If multiple update callback happened, and the function could not
     * be called sooner, it's OK to call it once after the last callback.
     *
     * If an update callback happens during or after this function, the function
     * must be called again at the soonest possible time.
     *
     * If MPV_RENDER_PARAM_ADVANCED_CONTROL was set, this will do additional work
     * such as allocating textures for the video decoder.
     *
     * @return a bitset of mpv_render_update_flag values (i.e. multiple flags are
     *         combined with bitwise or). Typically, this will tell the API user
     *         what should happen next. E.g. if the MPV_RENDER_UPDATE_FRAME flag is
     *         set, mpv_render_context_render() should be called. If flags unknown
     *         to the API user are set, or if the return value is 0, nothing needs
     *         to be done.
     */
    guint64 flags = mpv_render_context_update(app_data->mpv_gl);

    printf("emit_frame_ready - render context updated\n");

    if (flags & MPV_RENDER_UPDATE_FRAME)
    {
        if (!!gtk_widget_get_realized(GTK_WIDGET(app_data->gl_area))) {
    printf("GLArea is realized!\n");
}
        printf("emit_frame_ready - queueing gl_area render\n");
        gtk_gl_area_queue_render(app_data->gl_area); // g_signal_emit_by_name(model, "frame-ready");
    }

    return FALSE;
}

static void render_frame(AppData *app_data)
{
    printf("render_frame\n");

    if (app_data->mpv_gl == NULL)
    {
        printf("render_frame - rendered without mpv gl context\n");
        return;
    }

    if (app_data->gl_area == NULL)
    {
        printf("GLArea is NULL!\n");
        return;
    }
    if (!gtk_widget_get_realized(GTK_WIDGET(app_data->gl_area)))
    {
        printf("GLArea is not realized yet!\n");
        return;
    }

    printf("Attempting to get gl_area size\n");

    int width = gtk_widget_get_width(GTK_WIDGET(app_data->gl_area));
    int height = gtk_widget_get_height(GTK_WIDGET(app_data->gl_area));
    printf("Frame size: width:%d, height:%d\n", width, height);

    gint fbo = -1;
    glGetIntegerv(GL_FRAMEBUFFER_BINDING, &fbo);
    mpv_opengl_fbo fboParam = {fbo, width, height, 0};

    mpv_render_param params[] = {
        {MPV_RENDER_PARAM_OPENGL_FBO, &fboParam},
        {MPV_RENDER_PARAM_FLIP_Y, &(int){1}},
        {0, NULL}};

    printf("render frame - mpv_render_context_render\n");

    int status = mpv_render_context_render(app_data->mpv_gl, params);
    if (status < 0)
    {
        fprintf(stderr, "MPV failed to render frame: %s\n", mpv_error_string(status));
    }
    else
    {
        printf("render frame - rendered frame\n");
    }
}

static void init_mpv_render_context(AppData *app_data)
{

    mpv_opengl_init_params gl_init_params = {
        .get_proc_address = get_proc_address,
    };

    mpv_render_param params[] = {
        {MPV_RENDER_PARAM_API_TYPE, MPV_RENDER_API_TYPE_OPENGL},
        {MPV_RENDER_PARAM_OPENGL_INIT_PARAMS, &gl_init_params},
        {MPV_RENDER_PARAM_X11_DISPLAY, get_x11_display()},
        {MPV_RENDER_PARAM_WL_DISPLAY, get_wl_display()},
        {0, NULL}};

    printf("init_mpv_render_context - creating render context\n");
    check_error(mpv_render_context_create(&app_data->mpv_gl, app_data->mpv, params));
}

static void on_realize(GtkGLArea *area, gpointer user_data)
{
    AppData *app_data = (AppData *)user_data;

    gtk_gl_area_make_current(app_data->gl_area);

    if (gtk_gl_area_get_error(app_data->gl_area) != NULL)
    {
        g_warning("Failed to realize GLArea");
        return;
    }

    printf("GLArea realized successfully\n");

    init_mpv(app_data);
}

void init_mpv(AppData *app_data)
{
    if (app_data->gl_area == NULL)
    {
        printf("GLArea became NULL before rendering! - initmpv\n");
    }

    setlocale(LC_NUMERIC, "C");
    app_data->mpv = mpv_create();
    if (!app_data->mpv)
    {
        fprintf(stderr, "Failed to create MPV context.\n");
        exit(1);
    }

    check_error(mpv_set_option_string(app_data->mpv, "input-default-bindings", "yes"));
    check_error(mpv_set_option_string(app_data->mpv, "input-vo-keyboard", "yes"));
    check_error(mpv_set_option_string(app_data->mpv, "vo", "libmpv"));
    // check_error(mpv_set_option_string(app_data->mpv, "gpu-context", "wayland"));
    check_error(mpv_set_option_string(app_data->mpv, "hwdec", "auto"));
    check_error(mpv_set_option_string(app_data->mpv, "log-file", "mpv_log.log"));

    // Initialize MPV
    printf("init_mpv - Initializing mpv\n");
    check_error(mpv_initialize(app_data->mpv));

    // Set up MPV render context
    printf("init_mpv - Creating mpv render context\n");
    init_mpv_render_context(app_data);

    // Set up an event loop to handle MPV events
    printf("init_mpv - Registering process_mpv_events\n");

    g_idle_add(process_mpv_events, app_data);

    printf("init_mpv - registering callback\n");
    mpv_render_context_set_update_callback(app_data->mpv_gl, render_update_callback, app_data);

    const char *filePath = "/home/tom/Projects/python-mpv-gtk4/test.webm";
    // const char *filePath = "/home/tom/Downloads/test.h264";
    const char *cmd[] = {"loadfile", filePath, NULL};
    check_error(mpv_command(app_data->mpv, cmd));

    if (app_data->gl_area == NULL)
    {
        printf("GLArea became NULL before rendering! - initmpv end\n");
    }
    // const char *pauseCmd[] = {"cycle", "pause", NULL};
    // check_error(mpv_command(app_data->mpv, pauseCmd));
}

static void activate(GtkApplication *app, gpointer user_data)
{
    AppData *app_data = (AppData *)user_data;

    GtkWidget *overlay = gtk_overlay_new();
    gtk_widget_set_hexpand(overlay, TRUE);
    gtk_widget_set_vexpand(overlay, TRUE);

    // Drawing area
    /*app_data->area = gtk_drawing_area_new();
    gtk_widget_set_hexpand(app_data->area, TRUE);
    gtk_widget_set_vexpand(app_data->area, TRUE);
    gtk_overlay_set_child(GTK_OVERLAY(overlay), GTK_WIDGET(app_data->area));*/

    // GL AREA
    app_data->gl_area = GTK_GL_AREA(gtk_gl_area_new());
    gtk_widget_set_hexpand(GTK_WIDGET(app_data->gl_area), TRUE);
    gtk_widget_set_vexpand(GTK_WIDGET(app_data->gl_area), TRUE);
    gtk_gl_area_set_auto_render(app_data->gl_area, FALSE);

    g_signal_connect(app_data->gl_area, "realize", G_CALLBACK(on_realize), app_data);
    g_signal_connect(app_data->gl_area, "unrealize", G_CALLBACK(on_unrealize), app_data);
    g_signal_connect(app_data->gl_area, "render", G_CALLBACK(render_frame), app_data);

    gtk_overlay_set_child(GTK_OVERLAY(overlay), GTK_WIDGET(app_data->gl_area));

    // Create Casilda compositor
    /*char *socket = g_build_path(G_DIR_SEPARATOR_S, g_get_tmp_dir(), "popcorn-player.sock", NULL);
    CasildaCompositor *compositor = casilda_compositor_new(socket);
    gtk_overlay_set_child(GTK_OVERLAY(overlay), GTK_WIDGET(compositor));*/

    // WebKit
    /* GtkWidget *wv = webkit_web_view_new();
     WebKitWebView *webView = WEBKIT_WEB_VIEW(wv);
     webkit_web_view_load_uri(webView, "http://localhost:3000");
     WebKitSettings *settings = webkit_web_view_get_settings(webView);
     webkit_settings_set_enable_developer_extras(settings, TRUE);

     GdkRGBA transparent_color = {0, 0, 0, 0};  // RGBA with alpha = 0 for transparency
     webkit_web_view_set_background_color(webView, &transparent_color);

     WebKitWebInspector *inspector = webkit_web_view_get_inspector(webView);
     webkit_web_inspector_show(inspector);

     gtk_overlay_add_overlay(GTK_OVERLAY(overlay), GTK_WIDGET(wv));

     // Set the Wayland display for MPV to connect to Casilda compositor
     setenv("WAYLAND_DISPLAY", socket, 1);
 */

    GtkWidget *window = gtk_application_window_new(app);
    gtk_window_set_title(GTK_WINDOW(window), "GTK4 mpv Wrapper");
    gtk_window_set_default_size(GTK_WINDOW(window), 1280, 720);
    gtk_window_set_child(GTK_WINDOW(window), overlay);
    gtk_widget_show(window);

    if (app_data->gl_area == NULL)
    {
        printf("GLArea became NULL before rendering! - activate\n");
    }
    printf("Window is visible\n");

    // gtk_widget_grab_focus (GTK_WIDGET(webView));
}

int main(int argc, char **argv)
{
    AppData app_data = {0};

    setenv("MPV_VERBOSE", "v", 1); // Enable verbose mpv logging

    GtkApplication *app = gtk_application_new("org.example.App", G_APPLICATION_FLAGS_NONE);
    g_signal_connect(app, "activate", G_CALLBACK(activate), &app_data);

    int status = g_application_run(G_APPLICATION(app), argc, argv);

    g_object_unref(app);
    if (app_data.mpv_gl)
        mpv_render_context_free(app_data.mpv_gl);
    if (app_data.mpv)
        mpv_destroy(app_data.mpv);

    return status;
}

static void on_unrealize(GtkGLArea *area, gpointer user_data)
{
    printf("CALLING UNREALIZE!\n");
    AppData *app_data = (AppData *)user_data;
    if (app_data->mpv_gl)
    {
        mpv_render_context_free(app_data->mpv_gl);
        app_data->mpv_gl = NULL;
    }
}

static void *
get_proc_address(void *fn_ctx, const gchar *name)
{
    GdkDisplay *display = gdk_display_get_default();

#ifdef GDK_WINDOWING_WAYLAND
    if (GDK_IS_WAYLAND_DISPLAY(display))
        return eglGetProcAddress(name);
#endif
#ifdef GDK_WINDOWING_X11
    if (GDK_IS_X11_DISPLAY(display))
        return (void *)(intptr_t)
            glXGetProcAddressARB((const GLubyte *)name);
#endif
#ifdef GDK_WINDOWING_WIN32
    if (GDK_IS_WIN32_DISPLAY(display))
        return wglGetProcAddress(name);
#endif
    g_assert_not_reached();
    return NULL;
}

static void check_error(int status)
{
    if (status < 0)
    {
        fprintf(stderr, "mpv API error: %s\n", mpv_error_string(status));
        exit(1);
    }
}