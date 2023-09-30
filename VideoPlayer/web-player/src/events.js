/* eslint-disable no-use-before-define */
const handlers = {};
const sharedBufferHandlers = {};

export function postMessage(name, data = null) {
  console.log('Posting event:',name, data);
  window?.chrome?.webview?.postMessage({ name, data });
  //TODO error message, that it's intendend to be used in webview
}

export function registerEventHandler(name, handler) {
  if (!handlers[name]) {
    handlers[name] = [handler];
    return;
  }

  handlers[name].push(handler);
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
  const currentHandlers = handlers[eventData.name];

  if (currentHandlers) {
    for (let i = 0; i < currentHandlers.length; i++) {
      currentHandlers[i](eventData.data);
    }
    return;
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

let isInitialized = false;
export function initEvents() {
  if(isInitialized) return;

  postMessage('initialized');
  isInitialized = true;
}
