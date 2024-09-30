# Define project directories and output
BLD_DIR=build
LINUX_DIR=$(BLD_DIR)/Linux
POPCORN_LINUX_DIR=./Popcorn.Linux
POPCORN_PLAYER_LINUX_DIR=./Popcorn.Player.Linux
CONFIG=Release
DOTNET_VERSION=net8.0

# Clean output folders
clean:
	rm -rf $(BLD_DIR)

# Restore and build the Blazor Hybrid project
build-popcorn-linux:
	dotnet restore $(POPCORN_LINUX_DIR)
	dotnet build $(POPCORN_LINUX_DIR) --configuration $(CONFIG)

# Restore and build the GTK Video Player
build-popcorn-player-linux:
	dotnet restore $(POPCORN_PLAYER_LINUX_DIR)
	dotnet build $(POPCORN_PLAYER_LINUX_DIR) --configuration $(CONFIG)

# Bundle both projects together
bundle: build-popcorn-linux build-popcorn-player-linux
	mkdir -p $(LINUX_DIR)
	cp -r $(POPCORN_LINUX_DIR)/bin/$(CONFIG)/$(DOTNET_VERSION)/* $(LINUX_DIR)
	cp -r $(POPCORN_PLAYER_LINUX_DIR)/bin/$(CONFIG)/$(DOTNET_VERSION)/* $(LINUX_DIR)

# Default target: clean, build, and bundle
all: clean bundle

