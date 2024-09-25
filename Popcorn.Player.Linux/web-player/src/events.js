/* eslint-disable no-use-before-define */
const localHandlers = {};
const webviewHandlers = {};
const sharedBufferHandlers = {};

const webviewContext = window?.chrome?.webview;
let socket = null;


let isInitialized = false;
export function initEvents() {
  if (isInitialized) return;

  if (webviewContext) {
    webviewContext.addEventListener('message', eventHandler);
    webviewContext.addEventListener('sharedbufferreceived', bufferEventHandler);

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

    socket.onmessage = function (event) {
      const data = JSON.parse(event.data);
      console.log('Got message in socket',data.name);
      if(event.sharedBuffer){
        bufferEventHandler(event);
      } else {
        if(data.name === 'ping') {
          postMessage('pong');
        }
        eventHandler(event);
      }
    };
  }
}


export function postMessage(name, data = null) {
  console.log('Posting event:', name, data);
  const message = { name, data };
  if(webviewContext) {
    webviewContext.postMessage(message);
  } else {
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
  if (!sharedBufferHandlers[name]) {
    sharedBufferHandlers[name] = [handler];
    return;
  }

  sharedBufferHandlers[name].push(handler);
}

export function eventHandler(event) {
  const eventData = event.data;
  const currentHandlers = webviewHandlers[eventData.name];

  if (currentHandlers) {
    for (let i = 0; i < currentHandlers.length; i++) {
      currentHandlers[i](eventData.data);
    }
    return;
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

export function bufferEventHandler(event) {
  const currentArrayBufferHandlers = sharedBufferHandlers[event.additionalData.name]; // Assuming this array exists
  if (currentArrayBufferHandlers) {
    const arrayBuffer = event.getBuffer();
    for (let i = 0; i < currentArrayBufferHandlers.length; i++) {
      currentArrayBufferHandlers[i](arrayBuffer, event.additionalData);
    }
  } else {
    console.log('No handler found for event:', event.additionalData.name);
  }
}
