#include <mpv/client.h>
#include <glib-object.h>
#include "constants.h"
#include "app_data.h"

#ifdef GDK_WINDOWING_X11
#include <gdk/x11/gdkx.h>
#endif
#ifdef GDK_WINDOWING_WAYLAND
#include <gdk/wayland/gdkwayland.h>
#endif

static gboolean process_mpv_events(gpointer data)
{
    AppData *app_data = (AppData *)data;
    if (!app_data->mpv)
    {
        printf("mpv instance is NULL!\n");
        return FALSE;
    }

    gboolean done = FALSE;

    while (!done)
    {
        mpv_event *event = app_data->mpv ? mpv_wait_event(app_data->mpv, 0) : NULL;

        if (event)
        {
            printf("Received event: %s\n", mpv_event_name(event->event_id));

            done = !app_data->mpv ||
                   event->event_id == MPV_EVENT_SHUTDOWN ||
                   event->event_id == MPV_EVENT_NONE;

            if (!done)
            {
                if (event->event_id == MPV_EVENT_VIDEO_RECONFIG)
                {
                    printf("Video reconfigured: Adjusting viewport\n");
                    // Handle resizing or reconfiguring here
                }else if (event->event_id == MPV_EVENT_CLIENT_MESSAGE)
                {
                    printf("Event: client message\n");

                    mpv_event_client_message *msg = event->data;

                    done = msg->num_args == 2 &&
                           g_strcmp0(msg->args[0], MPV_ACTION) == 0 &&
                           g_strcmp0(msg->args[1], "win.quit") == 0;
                }
            }

            // emit in the future
            /*g_signal_emit_by_name(	app_data,
                        "mpv-event-notify",
                        event->event_id,
                        event->data );*/
        }
        else
        {
            done = TRUE;
        }
    }

    return FALSE;
}

void mpv_wakeup_callback(void *data)
{
    g_idle_add_full(G_PRIORITY_HIGH_IDLE, process_mpv_events, data, NULL);
}

gpointer get_wl_display(void)
{
    gpointer wl_display = NULL;

#ifdef GDK_WINDOWING_WAYLAND
    GdkDisplay *display = gdk_display_get_default();

    if (GDK_IS_WAYLAND_DISPLAY(display))
    {
        wl_display = gdk_wayland_display_get_wl_display(display);
    }
#endif

    return wl_display;
}

gpointer get_x11_display(void)
{
    gpointer x11_display = NULL;

#ifdef GDK_WINDOWING_X11
    GdkDisplay *display = gdk_display_get_default();

    if (GDK_IS_X11_DISPLAY(display))
    {
        x11_display = gdk_x11_display_get_xdisplay(display);
    }
#endif

    return x11_display;
}