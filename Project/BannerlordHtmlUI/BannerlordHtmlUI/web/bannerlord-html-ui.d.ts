export interface BannerlordHtmlUiPageContext {
  id: string | null;
  ownerId: string | null;
  lifecycle: string;
  isConsumer(): boolean;
  onLifecycle(handler: (info: unknown) => void): () => void;
}

export interface BannerlordHtmlUiScopePages {
  open(id: string): Promise<unknown>;
  close(): Promise<unknown>;
}

export interface BannerlordHtmlUiError extends Error {
  name: 'BannerlordHtmlUiError';
  code:
    | 'COMMAND_TIMEOUT'
    | 'REQUEST_TIMEOUT'
    | 'COMMAND_UNKNOWN'
    | 'REQUEST_UNKNOWN'
    | 'COMMAND_STALE'
    | 'COMMAND_UNREGISTERED'
    | 'REQUEST_STALE'
    | 'REQUEST_UNREGISTERED'
    | 'PROTOCOL_UNSUPPORTED_VERSION'
    | 'PROTOCOL_UNKNOWN_TYPE'
    | 'RUNTIME_DISPOSED'
    | 'PAGE_UNLOADED'
    | 'COMMAND_HANDLER_ERROR'
    | 'REQUEST_HANDLER_ERROR'
    | 'BRIDGE_ERROR';
  raw: string;
  operation: 'command' | 'request' | string;
  requestName: string | null;
}

export interface BannerlordHtmlUiI18n {
  readonly locale: string | null;
  getLocale(): Promise<string | null>;
  getLanguages(): Promise<unknown>;
  t(key: string, variables?: Record<string, unknown> | null, options?: { fallbackLanguage?: string | null }): Promise<string>;
  bind(root?: ParentNode): Promise<() => void>;
  formatDate(value: string | number | Date): Promise<string | undefined>;
  formatTime(value: string | number | Date): Promise<string | undefined>;
  onLocaleChanged(handler: (language: string | null) => void): () => void;
}

declare namespace BannerlordHtmlUI {
  interface BindingSchedulerOptions {
    debounce?: number;
    throttle?: number;
    event?: string;
    read?: (element: Element) => unknown;
    listenerOptions?: AddEventListenerOptions | boolean;
  }

  interface BindingApi {
    text(target: string | Element, key: string): () => void;
    value(target: string | Element, key: string): () => void;
    checked(target: string | Element, key: string): () => void;
    disabled(target: string | Element, key: string): () => void;
    hidden(target: string | Element, key: string): () => void;
    visible(target: string | Element, key: string): () => void;
    class(target: string | Element, className: string, key: string, truthy?: unknown): () => void;
    attr(target: string | Element, attribute: string, key: string): () => void;
    form(target: string | Element, map: Record<string, string>): () => void;
    twoWayValue(target: string | Element, key: string, writer: (value: unknown, event: Event, element: Element) => void, options?: BindingSchedulerOptions): () => void;
    twoWayChecked(target: string | Element, key: string, writer: (value: unknown, event: Event, element: Element) => void, options?: BindingSchedulerOptions): () => void;
    group(...disposers: Array<(() => void) | Array<() => void>>): () => void;
    debounce(writer: (value: unknown, event: Event, element: Element) => void, milliseconds: number): (value: unknown, event: Event, element: Element) => void;
    throttle(writer: (value: unknown, event: Event, element: Element) => void, milliseconds: number): (value: unknown, event: Event, element: Element) => void;
    list<T = unknown>(target: string | Element, key: string, render: (item: T, index: number, generation: number) => Element | { element: Element; dispose?: () => void; update?: (item: T, index: number, generation: number) => void } | null, options?: { clearOnDispose?: boolean; diff?: boolean; key?: (item: T, index: number) => string | number }): () => void;
    component(target: string | Element, factory: (props: Record<string, unknown>, root: Element) => Element | { element: Element; update?: (props: Record<string, unknown>) => void; dispose?: () => void } | null, props?: Record<string, unknown>): { element: Element | null; update(props?: Record<string, unknown>): void; dispose(): void };
    template<T = unknown>(target: string | Element, key: string, render: (item: T, index: number) => Element | { element: Element; update?: (item: T, index: number) => void; dispose?: () => void } | null, options?: { key?: (item: T, index: number) => string | number }): () => void;
    delegate(root: string | Element | Document, eventName: string, selector: string, handler: (event: Event, target: Element) => void, options?: AddEventListenerOptions): () => void;
    events(root: string | Element | Document, definitions: Record<string, { selector: string; handler: (event: Event, target: Element) => void; options?: AddEventListenerOptions } | Array<{ selector: string; handler: (event: Event, target: Element) => void; options?: AddEventListenerOptions }>>): () => void;
    apply(root?: ParentNode): () => void;
    dispose(): void;
  }

  interface GameState {
    get<T = unknown>(key: string): T | undefined;
    has(key: string): boolean;
    subscribe<T = unknown>(key: string, handler: (value: T) => void): () => void;
    snapshot(): Record<string, unknown>;
  }

  interface GameApp {
    readonly ownerId: string | null;
    call<T = unknown>(name: string, payload?: unknown, timeoutMs?: number): Promise<T>;
    request<T = unknown>(name: string, payload?: unknown, timeoutMs?: number): Promise<T>;
    on<T = unknown>(name: string, handler: (payload: T) => void): () => void;
    readonly state: GameState;
    readonly events: { on<T = unknown>(name: string, handler: (payload: T) => void): () => void };
    readonly page: BannerlordHtmlUiPageContext | null;
    readonly lifecycle: { readonly state: string | undefined; on(handler: (info: unknown) => void): () => void };
    readonly pageLifecycle?: { on(handler: (info: unknown) => void): () => void };
    readonly errors: { readonly last: unknown; on(handler: (error: unknown) => void): () => void };
    readonly input: WindowApi['input'] | null;
    readonly pages: BannerlordHtmlUiScopePages;
    readonly i18n: BannerlordHtmlUiI18n;
    readonly bind: BindingApi;
    app?: GameApp;
  }

  interface GameScope extends GameApp {
    readonly ownerId: string;
    call<T = unknown>(name: string, payload?: unknown, timeoutMs?: number): Promise<T>;
    request<T = unknown>(name: string, payload?: unknown, timeoutMs?: number): Promise<T>;
    on<T = unknown>(name: string, handler: (payload: T) => void): () => void;
    readonly state: GameState;
    readonly bind: BindingApi;
    readonly i18n: BannerlordHtmlUII18n;
  }

  interface WindowApi {
    readonly ownerId: string | null;
    scope(ownerId?: string): GameScope;
    call<T = unknown>(name: string, payload?: unknown, timeoutMs?: number): Promise<T>;
    request<T = unknown>(name: string, payload?: unknown, timeoutMs?: number): Promise<T>;
    on<T = unknown>(name: string, handler: (payload: T) => void): () => void;
    readonly state: GameState;
    readonly app: GameApp;
    readonly page: BannerlordHtmlUiPageContext | null;
    readonly lifecycle: { readonly state: string | undefined; on(handler: (info: unknown) => void): () => void };
    readonly errors: { readonly last: unknown; on(handler: (error: unknown) => void): () => void };
    readonly pages: BannerlordHtmlUiScopePages;
    readonly i18n: BannerlordHtmlUiI18n;
    readonly input: {
      capture(): Promise<unknown>;
      release(): Promise<unknown>;
      passive(): Promise<unknown>;
      setMode(mode: string): Promise<unknown>;
    };
    ready(): Promise<Record<string, unknown>>;
  }
}

declare global {
  interface Window {
    game: BannerlordHtmlUI.WindowApi;
  }
}

export {};
