/* eslint-disable no-use-before-define */
let localHandlers: any = {};

let isInitialized = false;
export function initEvents() {
  if (isInitialized) return;

  localHandlers = {};
  isInitialized = true;
}

export function postMessage(name: string, data: any = null) {
  console.log('Posting event:', name, data);
  localEventHandler(name, data);
}

export function registerEventHandler(name: string, handler: any) {
  if (!localHandlers[name]) {
    localHandlers[name] = [handler];
    return;
  }

  localHandlers[name].push(handler);
}

function localEventHandler(name: string, data: any) {
  const currentHandlers = localHandlers[name];

  if (currentHandlers) {
    for (let i = 0; i < currentHandlers.length; i++) {
      currentHandlers[i](data);
    }
  }
}

export function unregisterEventHandler(name: string, handler: any) {
  if (!localHandlers[name]) return;

  localHandlers[name] = localHandlers[name].filter((h) => h !== handler);

  if (!localHandlers[name].length) {
    delete localHandlers[name];
  }
}
