interface DialogStubOptions {
  /** Focus/accessibility suites need the native close notification; simpler suites do not. */
  dispatchCloseEvent?: boolean;
  methods?: readonly ("showModal" | "close")[];
}

/** Install only the requested jsdom dialog behavior, restoring the original descriptors afterwards. */
export function installDialogStub({ dispatchCloseEvent = false, methods = ["showModal", "close"] }: DialogStubOptions = {}) {
  const prototype = HTMLDialogElement.prototype;
  const descriptors = methods.map(method => [method, Object.getOwnPropertyDescriptor(prototype, method)] as const);
  for (const method of methods) {
    Object.defineProperty(prototype, method, {
      configurable: true,
      writable: true,
      value: function (this: HTMLDialogElement) {
        this.open = method === "showModal";
        if (method === "close" && dispatchCloseEvent) this.dispatchEvent(new Event("close"));
      },
    });
  }
  return () => {
    for (const [method, descriptor] of descriptors) {
      if (descriptor) Object.defineProperty(prototype, method, descriptor);
      else Reflect.deleteProperty(prototype, method);
    }
  };
}
