BLD_DIR=build
LINUX_DIR=$(BLD_DIR)/Linux
POPCORN_DIR_LINUX=./Popcorn.Linux
POPCORN_PLAYER_DIR_LINUX=./Popcorn.Player.Linux
MPVSHARP_DIR=./MPVSharp
MPVSHARP_SRC_DIR=$(MPVSHARP_DIR)/src
MPVSHARP_BIN_DIR=$(MPVSHARP_DIR)/bin

POPCORN_BIN_DIR=$(POPCORN_DIR_LINUX)/bin/$(CONFIG)/$(DOTNET_VERSION)
POPCORN_PLAYER_BIN_DIR=$(POPCORN_PLAYER_DIR_LINUX)/bin/$(CONFIG)/$(DOTNET_VERSION)

CONFIG=Release
DOTNET_VERSION=net9.0

# Clean output folders
clean:
	rm -rf $(BLD_DIR) $(MPVSHARP_BIN_DIR) $(POPCORN_BIN_DIR) $(POPCORN_PLAYER_BIN_DIR)

# Pull and build MPVSharp
update-mpvsharp:
	git submodule update --init --recursive $(MPVSHARP_DIR)
	git submodule update --remote $(MPVSHARP_DIR)

	# Restore and build MPVSharp
	dotnet restore $(MPVSHARP_DIR)/src/Nickvision.MPVSharp/
	dotnet build $(MPVSHARP_DIR)/src/Nickvision.MPVSharp/ --configuration $(CONFIG)

	# Copy the built DLL to the bin directory
	cp $(MPVSHARP_DIR)/src/Nickvision.MPVSharp/bin/$(CONFIG)/net7.0/Nickvision.MPVSharp.* $(MPVSHARP_DIR)

build-popcorn-web:
	cd ./Popcorn.Web
	npm run build

build-popcorn-linux build-popcorn-web:
	dotnet restore $(POPCORN_DIR_LINUX)
	dotnet build $(POPCORN_DIR_LINUX) --configuration $(CONFIG)

build-popcorn-player-linux:
	dotnet restore $(POPCORN_PLAYER_DIR_LINUX)
	dotnet build $(POPCORN_PLAYER_DIR_LINUX) --configuration $(CONFIG)

# Bundle both projects together
bundle-linux: build-popcorn-linux build-popcorn-player-linux
	mkdir -p $(LINUX_DIR)
	cp -r $(POPCORN_BIN_DIR)/* $(LINUX_DIR)
	cp -r $(POPCORN_PLAYER_BIN_DIR)/* $(LINUX_DIR)

build-player-web:
	cd ./Popcorn.Player.Web
	npm run build

all: clean build-player-web bundle-linux
