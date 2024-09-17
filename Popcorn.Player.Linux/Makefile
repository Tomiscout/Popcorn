# Compiler and flags
CC = gcc
CFLAGS = -Wall -Wextra `pkg-config --cflags gtk4 mpv epoxy webkitgtk-6.0  casilda-0.1`
LDFLAGS = -L $(LIB_DIR) `pkg-config --libs gtk4 mpv epoxy webkitgtk-6.0  casilda-0.1`

# Directories
SRC_DIR = src
BIN_DIR = bin
LIB_DIR = lib
CASILDA_DIR = casilda
CASILDA_BUILD_DIR = _build
CASILDA_INSTALL_DIR = _install
PKGCONFIG_DIR = $(LIB_DIR)/pkgconfig
INSTALL_DIR = $(PWD)

export PKG_CONFIG_PATH := $(PKGCONFIG_DIR)
export DRI_PRIME=1
export __NV_PRIME_RENDER_OFFLOAD=1
export __GLX_VENDOR_LIBRARY_NAME=nvidia
export GDK_BACKEND=wayland

# Source files
SRCS = $(wildcard $(SRC_DIR)/*.c)

# Object files (compiled from source)
OBJS = $(SRCS:$(SRC_DIR)/%.c=$(LIB_DIR)/%.o)

# Target executable
TARGET = $(BIN_DIR)/popcorn-gtk

# Default rule (build everything)
all: install_casilda $(TARGET)

# Rule to build the target executable
$(TARGET): $(OBJS)
	mkdir -p $(BIN_DIR)
	$(CC) -o $@ $(OBJS) $(LDFLAGS)

# Rule to compile .c files into .o files
$(LIB_DIR)/%.o: $(SRC_DIR)/%.c
	mkdir -p $(LIB_DIR)
	$(CC) $(CFLAGS) -c $< -o $@

# Install Casilda .so and pkgconfig file
install_casilda:
	mkdir -p $(PKGCONFIG_DIR)
	cd $(CASILDA_DIR) && meson setup --prefix=$(INSTALL_DIR)/$(CASILDA_DIR)/$(CASILDA_INSTALL_DIR) ./$(CASILDA_BUILD_DIR)
	cd $(CASILDA_DIR) && ninja -C $(CASILDA_BUILD_DIR) install

	mv $(CASILDA_DIR)/$(CASILDA_INSTALL_DIR)/lib/libcasilda-0.1.so $(LIB_DIR)/libcasilda-0.1.so
	mv $(CASILDA_DIR)/$(CASILDA_INSTALL_DIR)/lib/pkgconfig/casilda-0.1.pc $(PKGCONFIG_DIR)/casilda-0.1.pc

# Run the application after build
run: $(TARGET)
	@echo -e "Running Popcorn.Player.Linux...\n\n"
	LD_LIBRARY_PATH=$(LIB_DIR):$$LD_LIBRARY_PATH ./$(TARGET)

# Clean up compiled files
clean:
	rm -rf $(LIB_DIR)/*.o $(BIN_DIR)/* $(CASILDA_DIR)/$(CASILDA_BUILD_DIR)/* $(CASILDA_DIR)/$(CASILDA_INSTALL_DIR)/*
