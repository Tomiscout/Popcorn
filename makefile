# Define project directories and output
BLD_DIR=build
LINUX_DIR=$(BLD_DIR)/Linux
POPCORN_DIR_LINUX=./Popcorn.Linux
POPCORN_PLAYER_DIR_LINUX=./Popcorn.Player.Linux

CONFIG=Release
DOTNET_VERSION=net8.0

# Clean output folders
clean:
	rm -rf $(BLD_DIR)

build-popcorn-linux:
	dotnet restore $(POPCORN_DIR_LINUX)
	dotnet build $(POPCORN_DIR_LINUX) --configuration $(CONFIG)

build-popcorn-player-linux:
	dotnet restore $(POPCORN_PLAYER_DIR_LINUX)
	dotnet build $(POPCORN_PLAYER_DIR_LINUX) --configuration $(CONFIG)

# Bundle both projects together
bundle-linux: build-popcorn-linux build-popcorn-player-linux
	mkdir -p $(LINUX_DIR)
	cp -r $(POPCORN_DIR_LINUX)/bin/$(CONFIG)/$(DOTNET_VERSION)/* $(LINUX_DIR)
	cp -r $(POPCORN_PLAYER_DIR_LINUX)/bin/$(CONFIG)/$(DOTNET_VERSION)/* $(LINUX_DIR)

build-player-web:
	cd ./Popcorn.Player.Web
	npm run build

all: clean build-player-web bundle-linux
