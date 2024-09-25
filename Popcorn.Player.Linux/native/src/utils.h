#ifndef UTILS_H
#define UTILS_H

// Function prototype
void mpv_wakeup_callback(void *data);
gpointer get_wl_display(void);
gpointer get_x11_display(void);
gboolean process_mpv_events(gpointer data);

#endif // UTILS_H
