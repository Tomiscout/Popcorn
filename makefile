# Short names for `cargo xtask` (xtask/: the build itself, the same on Linux and Windows; Docs/Building.md), plus the
# Linux-only install of the bundle (systemd user service, desktop entry).
#   make all VERSION=1.2.3 MPV_LIB=/path/libmpv.so.2 CEF_PATH=~/.cache/popcorn/cef
# Everything is incremental: `make clean` (or `make rebuild`) for a fresh bundle.
XTASK=cargo xtask
BUILD_ARGS=$(if $(VERSION),--version $(VERSION)) $(if $(MPV_LIB),--mpv-lib $(MPV_LIB)) $(if $(CEF_PATH),--cef-path $(CEF_PATH))

BLD_DIR=Bundle
LINUX_DIR=$(BLD_DIR)/Linux
POPCORN_DIR_LINUX=./Popcorn.Linux
POPCORN_SERVER_DIR=./Popcorn.Server
INSTALL_DIR=$(HOME)/.local/bin/Popcorn
SYSTEMD_USER_DIR=$(HOME)/.config/systemd/user
SYSTEMD_ENV_DIR=$(HOME)/.config/popcorn
SYSTEMD_SERVICE_NAME=popcorn.service
SYSTEMD_ENV_NAME=popcorn.env
SYSTEMD_SRC_DIR=$(POPCORN_SERVER_DIR)/systemd
SYSTEMD_SERVICE_FILE=$(SYSTEMD_USER_DIR)/$(SYSTEMD_SERVICE_NAME)
SYSTEMD_ENV_FILE=$(SYSTEMD_ENV_DIR)/$(SYSTEMD_ENV_NAME)
DESKTOP_DIR=$(HOME)/.local/share/applications
DESKTOP_FILE=$(DESKTOP_DIR)/popcorn.desktop

.PHONY: all rebuild clean check test mpv update-mpv release build-popcorn-web build-player-web build-popcorn-server \
	build-popcorn-linux build-popcorn-player build-popcorn-installer build-popcorn-cli build-plugins kill-popcorn install-linux

# Build everything + bundle into Bundle/Linux/ (incl. the CEF runtime)
all:
	$(XTASK) bundle $(BUILD_ARGS)

rebuild: clean all

clean:
	$(XTASK) clean

# Build all projects, tests, web type check + lint, clippy
check:
	$(XTASK) check

test:
	$(XTASK) test

# libmpv with the DMA-BUF render API (MPV/libmpv.so.2), rebuilt when MPV/src or its build options changed. Optional:
# once built, every bundle keeps it current and ships it.
mpv:
	$(XTASK) mpv

# Move the MPV/src submodule to the fork's latest master and rebuild libmpv
update-mpv:
	$(XTASK) mpv --update

# make release ARGS="--publish --draft"
release:
	$(XTASK) release $(BUILD_ARGS) $(ARGS)

build-popcorn-web:
	$(XTASK) build web

build-player-web:
	$(XTASK) build player-web

build-popcorn-server:
	$(XTASK) build server $(BUILD_ARGS)

build-popcorn-linux:
	$(XTASK) build desktop $(BUILD_ARGS)

build-popcorn-player:
	$(XTASK) build player $(BUILD_ARGS)

build-popcorn-installer:
	$(XTASK) build installer

build-popcorn-cli:
	$(XTASK) build cli

# Plugins in Plugins/* are separate products, not part of the app build: each is assembled into
# Popcorn.Server/plugins/<id>/ (see its csproj), which the server's build ships when present. Run before `make all`
# to bundle them. Their UI modules link Popcorn.Web's @popcorn/plugin-sdk, hence its node_modules.
build-plugins:
	test -d Popcorn.Web/node_modules || (cd Popcorn.Web && npm ci)
	@for dir in $(wildcard ./Plugins/*/); do \
		if [ -f $$dir/ui/package.json ]; then (cd $$dir/ui && npm ci && npm run build) || exit 1; fi; \
		dotnet build $$dir --configuration Release || exit 1; \
	done

# Stop running installed instances: desktop shell (+ CEF subprocesses, same exe), native player and a server the
# shell spawned outside systemd. Replacing the files under them otherwise leaves the app frozen.
# The pattern is anchored to argv[0] so it never matches this recipe's own shell.
POPCORN_PROCS='^$(INSTALL_DIR)/Popcorn\.(Linux|Player\.Linux|Server)( |$$)'
kill-popcorn:
	@if pgrep -f $(POPCORN_PROCS) >/dev/null; then \
		echo "Stopping running Popcorn instances"; \
		pkill -TERM -f $(POPCORN_PROCS); \
		for i in 1 2 3 4 5 6; do pgrep -f $(POPCORN_PROCS) >/dev/null || exit 0; sleep 0.5; done; \
		echo "Still running after 3s, killing"; \
		pkill -KILL -f $(POPCORN_PROCS) || true; \
	fi

install-linux:
	@test -x $(LINUX_DIR)/Popcorn.Server || { echo "$(LINUX_DIR)/Popcorn.Server missing, run 'make all' first"; exit 1; }
	@dotnet --list-runtimes | grep -q '^Microsoft.AspNetCore.App 10\.' || \
		{ echo "ASP.NET Core 10 runtime missing (needed by Popcorn.Server). Arch: sudo pacman -S aspnet-runtime"; exit 1; }
	systemctl --user stop $(SYSTEMD_SERVICE_NAME) 2>/dev/null || true
	$(MAKE) --no-print-directory kill-popcorn
	rm -rf $(INSTALL_DIR)
	mkdir -p $(INSTALL_DIR)
	cp -r $(LINUX_DIR)/* $(INSTALL_DIR)
	mkdir -p $(DESKTOP_DIR)
	test -f $(DESKTOP_FILE) || sed -e 's|@INSTALL_DIR@|$(INSTALL_DIR)|g' $(POPCORN_DIR_LINUX)/popcorn.desktop > $(DESKTOP_FILE)
	mkdir -p $(SYSTEMD_USER_DIR) $(SYSTEMD_ENV_DIR)
	test -f $(SYSTEMD_ENV_FILE) || cp $(SYSTEMD_SRC_DIR)/$(SYSTEMD_ENV_NAME) $(SYSTEMD_ENV_FILE)
	sed \
		-e 's|@INSTALL_DIR@|$(INSTALL_DIR)|g' \
		-e 's|@ENV_FILE@|$(SYSTEMD_ENV_FILE)|g' \
		$(SYSTEMD_SRC_DIR)/$(SYSTEMD_SERVICE_NAME) > $(SYSTEMD_SERVICE_FILE)
	systemctl --user daemon-reload
	systemctl --user enable --now $(SYSTEMD_SERVICE_NAME)
