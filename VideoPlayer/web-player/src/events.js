/* eslint-disable no-use-before-define */
const handlers = {};

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

export function eventHandler(event) {
  const currentHandlers = handlers[event.data.name];
  if (!currentHandlers) {
    console.log('No handler found for event:', event.data.name);
    return;
  }

  for (let i = 0; i < currentHandlers.length; i++) {
    currentHandlers[i](event.data.data);
  }
}

let isInitialized = false;
export function initEvents() {
  if(isInitialized) return;

  postMessage('initialized');
  isInitialized = true;
}
