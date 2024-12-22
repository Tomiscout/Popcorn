/* eslint-disable no-use-before-define */
const localHandlers = {};
const webviewHandlers = {};
const sharedBufferHandlers = [];

const webviewContext = window?.chrome?.webview;
let socket = null;


let isInitialized = false;
export function initEvents() {
  if (isInitialized) return;

  if (webviewContext) {
    webviewContext.addEventListener('message', eventHandlerChromium);
    webviewContext.addEventListener('sharedbufferreceived', bufferEventHandlerWindows);

    postMessage('initialized');
    isInitialized = true;
  } else if (window.wsAddress) {
    socket = new WebSocket(window.wsAddress);

    socket.onopen = function () {
      console.log("WebSocket connection opened");
      postMessage('initialized');
      isInitialized = true;
    };
    socket.onerror = function (error) {
      console.log("WebSocket error: ", error);
    };
    socket.onclose = function () {
      console.log("WebSocket connection closed");
    };

    socket.onmessage = async function (event) {
      if (event.data instanceof Blob) {
        const blobUrl = URL.createObjectURL(event.data);
        /*const arrayBuffer = await event.data.arrayBuffer();

        const dataView = new DataView(arrayBuffer);

        // Read the first 4 bytes as int32 (JSON length)
        const jsonLength = dataView.getUint32(0, true); // true for little-endian

        // Extract the JSON bytes and decode to a string
        const jsonBytes = new Uint8Array(arrayBuffer, 4, jsonLength);
        const jsonString = new TextDecoder("utf-8").decode(jsonBytes);
        const jsonData = JSON.parse(jsonString);

        // Extract the remaining binary data (image)
        const imageBytes = new Uint8Array(arrayBuffer, 4 + jsonLength);
        const blob = new Blob([imageBytes]);
        const imageUrl = URL.createObjectURL(blob);*/

        bufferEventHandler(blobUrl);
      } else {
        const data = JSON.parse(event.data);
        eventHandler(data);
      }
    };
  }
}

export function postLocalMessage(name, data = null) {
  localEventHandler(name, data);
}

export function postMessage(name, data = null) {
  console.log('Posting event:', name, data);
  const message = { name, data };
  if(webviewContext) {
    webviewContext.postMessage(message);
  } else if(socket) {
    socket.send(JSON.stringify(message));
  }
  
  localEventHandler(name, data);
}

export function registerEventHandler(name, handler) {
  if (!localHandlers[name]) {
    localHandlers[name] = [handler];
    return;
  }

  localHandlers[name].push(handler);
}

export function registerWebviewEventHandler(name, handler) {
  if (!webviewHandlers[name]) {
    webviewHandlers[name] = [handler];
    return;
  }

  webviewHandlers[name].push(handler);
}

export function registerSharedBufferHandler(name, handler) {
  sharedBufferHandlers.push(handler);
}

function eventHandlerChromium(webEvent) {
  eventHandler(webEvent.data);
}
export function eventHandler(webEvent) {
  const currentHandlers = webviewHandlers[webEvent.name];
  if(webEvent.name !== 'current_time') {
    console.log('Received event', webEvent);
  }

  if (currentHandlers) {
    for (let i = 0; i < currentHandlers.length; i++) {
      currentHandlers[i](webEvent.data);
    }
    return;
  } else {
    console.log('Received unknown event', webEvent.name, webEvent.data);
  }
}

function localEventHandler(name, data) {
  const currentHandlers = localHandlers[name];

  if (currentHandlers) {
    for (let i = 0; i < currentHandlers.length; i++) {
      currentHandlers[i](data);
    }
  }
}

export function bufferEventHandlerWindows(event) {
  const buffer = event.getBuffer();
  const blobUrl = URL.createObjectURL(new Blob([buffer], { type: 'image/jpeg' }));
  bufferEventHandler(blobUrl);
}

export function bufferEventHandler(blobUrl) {
  for(const handler of sharedBufferHandlers) {
    handler(blobUrl);
  }
}

export function unregisterEventHandler(name, handler) {
  if (!localHandlers[name]) return;

  localHandlers[name] = localHandlers[name].filter((h) => h !== handler);

  if (!localHandlers[name].length) {
    delete localHandlers[name];
  }
}

export function unregisterWebviewEventHandler(name, handler) {
  if (!webviewHandlers[name]) return;

  webviewHandlers[name] = webviewHandlers[name].filter((h) => h !== handler);

  if (!webviewHandlers[name].length) {
    delete webviewHandlers[name];
  }
}