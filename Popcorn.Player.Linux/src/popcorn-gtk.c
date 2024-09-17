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

#include "casilda-compositor.h"

static void check_error(int status)
{
    if (status < 0)
    {
        fprintf(stderr, "mpv API error: %s\n", mpv_error_string(status));
        exit(1);
    }
}
/*
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

static gboolean emit_frame_ready(gpointer data)
{
    printf("Frame ready!\n");

    AppData *app_data = (AppData *)data;
    guint64 flags = mpv_render_context_update(app_data->mpv_gl);

    if (flags & MPV_RENDER_UPDATE_FRAME)
    {
        gtk_gl_area_queue_render(GTK_GL_AREA(app_data->gl_area));
    }

    return FALSE;
}

static void render_update_callback(gpointer app_data)
{
    g_idle_add_full(G_PRIORITY_HIGH,
                    emit_frame_ready,
                    app_data,
                    NULL);
}

static void on_realize(GtkGLArea *area, gpointer user_data)
{
    AppData *app_data = (AppData *)user_data;

    gtk_gl_area_make_current(area);

    mpv_opengl_init_params gl_init_params = {
        .get_proc_address = get_proc_address,
    };
    mpv_render_param params[] = {
        //{MPV_RENDER_PARAM_API_TYPE, MPV_RENDER_API_TYPE_OPENGL},
        //{MPV_RENDER_PARAM_OPENGL_INIT_PARAMS, &gl_init_params},
        //{MPV_RENDER_PARAM_WL_DISPLAY, get_wl_display()},
        //{MPV_RENDER_PARAM_X11_DISPLAY, get_x11_display()},
        {MPV_RENDER_PARAM_WL_DISPLAY, "/tmp/popcorn-player.sock"},
        {0, NULL}};

    int res = mpv_render_context_create(&app_data->mpv_gl, app_data->mpv, params);
    if (res < 0)
    {
        printf("Failed to initialize mpv GL context: %s\n", mpv_error_string(res));
        return;
    }

    mpv_render_context_set_update_callback(app_data->mpv_gl, render_update_callback, app_data);
}

static void on_unrealize(GtkGLArea *area, gpointer user_data)
{
    AppData *app_data = (AppData *)user_data;
    if (app_data->mpv_gl)
    {
        mpv_render_context_free(app_data->mpv_gl);
        app_data->mpv_gl = NULL;
    }
}

static gboolean render(GtkGLArea *area, GdkGLContext *context, gpointer user_data)
{
    AppData *app_data = (AppData *)user_data;
    printf("Calling render\n");

    if (!app_data->mpv_gl)
        return FALSE;

    gtk_gl_area_make_current(area);
    if (gtk_gl_area_get_error(area) != NULL)
    {
        return FALSE;
    }

    int width = gtk_widget_get_width(GTK_WIDGET(area));
    int height = gtk_widget_get_height(GTK_WIDGET(area));

    int fbo_id = 0;
    glGetIntegerv(GL_FRAMEBUFFER_BINDING, &fbo_id);
    printf("FBO: %i\n", fbo_id);

    mpv_opengl_fbo fbo = {fbo_id, width, height, 0};
    int flip_y = 1;

    mpv_render_param params[] = {
        {MPV_RENDER_PARAM_OPENGL_FBO, &fbo},
        {MPV_RENDER_PARAM_FLIP_Y, &flip_y},
        {0}};

    int err = mpv_render_context_render(app_data->mpv_gl, params);
    if (err < 0)
    {
        printf("mpv failed to render frame: %s\n", mpv_error_string(err));
    }
    else
    {
        printf("mpv frame rendered successfully\n");
    }

    return TRUE;
}*/

static void activate(GtkApplication *app, gpointer user_data)
{
    AppData *app_data = (AppData *)user_data;

    GtkWidget *window = gtk_application_window_new(app);
    gtk_window_set_title(GTK_WINDOW(window), "GTK4 mpv Wrapper");
    gtk_window_set_default_size(GTK_WINDOW(window), 1280, 720);

    // Create Casilda compositor
    char *socket = g_build_path(G_DIR_SEPARATOR_S, g_get_tmp_dir(), "popcorn-player.sock", NULL);
    CasildaCompositor *compositor = casilda_compositor_new(socket);

    GtkWidget *overlay = gtk_overlay_new();
    gtk_widget_set_hexpand(overlay, TRUE);
    gtk_widget_set_vexpand(overlay, TRUE);

    gtk_overlay_set_child(GTK_OVERLAY(overlay), GTK_WIDGET(compositor));
    gtk_window_set_child(GTK_WINDOW(window), overlay);

    // WebKit
    GtkWidget *wv = webkit_web_view_new();
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

    gtk_widget_show(window);
    gtk_widget_grab_focus (GTK_WIDGET(webView));

    // Load a video file (replace with your video file path)
    //const string filePath = "/home/tom/Projects/python-mpv-gtk4/test.webm";
    const char *filePath = "/home/tom/Downloads/test.h264";
    const char *cmd[] = {"loadfile", filePath, NULL};
    check_error(mpv_command(app_data->mpv, cmd));

    //const char *pauseCmd[] = {"cycle", "pause", NULL};
    //check_error(mpv_command(app_data->mpv, pauseCmd));
}

int main(int argc, char **argv)
{
    AppData app_data = {0};

    setenv("MPV_VERBOSE", "v", 1); // Enable verbose mpv logging

    app_data.mpv = mpv_create();
    if (!app_data.mpv)
    {
        printf("Failed to create mpv context.\n");
        return 1;
    }

    //check_error(mpv_set_option_string(app_data.mpv, "input-default-bindings", "yes"));
    //check_error(mpv_set_option_string(app_data.mpv, "input-vo-keyboard", "yes"));
    //mpv_set_option_string(app_data.mpv, "gpu-api", "vulkan");
    // mpv_set_option_string(app_data.mpv, "gpu-debug", "yes");
    mpv_set_option_string(app_data.mpv, "vo", "gpu");
    mpv_set_option_string(app_data.mpv, "gpu-context", "wayland");
    mpv_set_option_string(app_data.mpv, "hwdec", "auto");
    mpv_set_option_string(app_data.mpv, "log-file", "mpv_log.log");

    mpv_set_wakeup_callback(app_data.mpv, mpv_wakeup_callback, &app_data);
    int res = mpv_initialize(app_data.mpv);
    if (res < 0)
    {
        printf("Failed to initialize mpv context: %s\n", mpv_error_string(res));
        return 1;
    }

    GtkApplication *app = gtk_application_new("org.example.App", G_APPLICATION_FLAGS_NONE);
    g_signal_connect(app, "activate", G_CALLBACK(activate), &app_data);

    int status = g_application_run(G_APPLICATION(app), argc, argv);

    g_object_unref(app);
    if (app_data.mpv)
        mpv_destroy(app_data.mpv);

    return status;
}