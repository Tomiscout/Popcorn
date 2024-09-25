#ifndef APP_DATA_H
#define APP_DATA_H

#include <mpv/client.h>
#include <mpv/render_gl.h>
#include <gtk/gtk.h>

typedef struct {
    mpv_handle *mpv;
    mpv_render_context *mpv_gl;
    GtkGLArea *gl_area;
} AppData;

#endif // APP_DATA_H
