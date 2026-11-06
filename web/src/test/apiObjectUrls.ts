/** Deterministic stand-in for lib/apiObjectUrls in component tests: `blob:<api path>`, resolved immediately. */
export const fakeObjectUrls = {
  acquireObjectUrl: (path: string) => ({ url: Promise.resolve(`blob:${path}`), release: () => undefined }),
  objectUrlStamp: () => 0,
  subscribeObjectUrls: () => () => undefined,
};
