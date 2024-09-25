# Compiler and flags
CC = gcc
CFLAGS = -Wall -Wextra `pkg-config --cflags gtk4 mpv epoxy webkitgtk-6.0 `
LDFLAGS = -L $(LIB_DIR) `pkg-config --libs gtk4 mpv epoxy webkitgtk-6.0 `

# Directories
SRC_DIR = src
BIN_DIR = bin
LIB_DIR = lib
PKGCONFIG_DIR = $(LIB_DIR)/pkgconfig
INSTALL_DIR = $(PWD)

export PKG_CONFIG_PATH := $(PKGCONFIG_DIR)
#export DRI_PRIME=1
#export __NV_PRIME_RENDER_OFFLOAD=1
#export __GLX_VENDOR_LIBRARY_NAME=nvidia
export GDK_BACKEND=wayland
#export __EGL_VENDOR_LIBRARY_FILENAMES=/usr/share/glvnd/egl_vendor.d/10_nvidia.json 
#__NV_PRIME_RENDER_OFFLOAD=1 __GLX_VENDOR_LIBRARY_NAME=nvidia GDK_BACKEND=wayland gdb ./bin/popcorn-gtk
export EGL_LOG_LEVEL=debug
export WAYLAND_DISPLAY=wayland-0


# Source files
SRCS = $(wildcard $(SRC_DIR)/*.c)

# Object files (compiled from source)
OBJS = $(SRCS:$(SRC_DIR)/%.c=$(LIB_DIR)/%.o)

# Target executable
TARGET = $(BIN_DIR)/popcorn-gtk

# Default rule (build everything)
all: $(TARGET)

# Rule to build the target executable
$(TARGET): $(OBJS)
	mkdir -p $(BIN_DIR)
	$(CC) -o $@ $(OBJS) $(LDFLAGS)

# Rule to compile .c files into .o files
$(LIB_DIR)/%.o: $(SRC_DIR)/%.c
	mkdir -p $(LIB_DIR)
	$(CC) $(CFLAGS) -c $< -o $@

# Run the application after build
run: $(TARGET)
	@echo -e "Running Popcorn.Player.Linux...\n\n"
	LD_LIBRARY_PATH=$(LIB_DIR):$$LD_LIBRARY_PATH ./$(TARGET)

# Clean up compiled files
clean:
	rm -rf $(LIB_DIR)/*.o $(BIN_DIR)/*
