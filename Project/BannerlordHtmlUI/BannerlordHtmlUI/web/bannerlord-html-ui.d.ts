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

export interface BannerlordHtmlUiI18n {
  readonly locale: string | null;
  getLocale(): Promise<string | null>;
  getLanguages(): Promise<unknown>;
  t(key: string, variables?: Record<string, unknown> | null, options?: { fallbackLanguage?: string | null }): Promise<string>;
  bind(root?: Document | ParentNode): Promise<() => void>;
  formatDate(value: string | number | Date): Promise<string>;
  formatTime(value: string | number | Date): Promise<string>;
  onLocaleChanged(handler: (locale: string | null) => void): () => void;
}

declare namespace BannerlordHtmlUI {
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
    list<T = unknown>(target: string | Element, key: string, render: (item: T, index: number, generation: number) => Element | { element: Element; dispose?: () => void } | null, options?: { clearOnDispose?: boolean; diff?: boolean; key?: (item: T, index: number) => string | number }): () => void;
    component(target: string | Element, factory: (props: Record<string, unknown>, root: Element) => Element | { element: Element; update?: (props: Record<string, unknown>) => void; dispose?: () => void } | null, props?: Record<string, unknown>): { element: Element | null; update(props?: Record<string, unknown>): void; dispose(): void };
    template<T = unknown>(target: string | Element, key: string, render: (item: T, index: number) => Element | { element: Element; update?: (item: T, index: number) => void; dispose?: () => void } | null, options?: { key?: (item: T, index: number) => string | number }): () => void;
    twoWayValue(target: string | Element, key: string, onChange?: (value: string) => void, options?: { event?: string; debounce?: number }): () => void;
    twoWayChecked(target: string | Element, key: string, onChange?: (value: boolean) => void): () => void;
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

  interface LifecycleApi {
    readonly state: string | undefined;
    on(handler: (info: unknown) => void): () => void;
  }

  interface ErrorApi {
    readonly last: unknown;
    on(handler: (error: unknown) => void): () => void;
  }

  interface InputApi {
    capture(): Promise<unknown>;
    release(): Promise<unknown>;
    passive(): Promise<unknown>;
    setMode(mode: string): Promise<unknown>;
  }

  interface GameApp {
    readonly ownerId: string | null;
    call<T = unknown>(name: string, payload?: unknown, timeoutMs?: number): Promise<T>;
    request<T = unknown>(name: string, payload?: unknown, timeoutMs?: number): Promise<T>;
    on<T = unknown>(name: string, handler: (payload: T) => void): () => void;
    readonly state: GameState;
    readonly events: { on<T = unknown>(name: string, handler: (payload: T) => void): () => void };
    readonly page: BannerlordHtmlUiPageContext | null;
    readonly lifecycle: LifecycleApi;
    readonly pageLifecycle: { on(handler: (info: unknown) => void): () => void };
    readonly errors: ErrorApi;
    readonly input: InputApi;
    readonly pages: BannerlordHtmlUiScopePages;
    readonly app: GameApp;
    readonly bind: BindingApi;
    readonly i18n: BannerlordHtmlUiI18n;
  }

  interface GameScope extends GameApp {
    readonly ownerId: string;
    call<T = unknown>(name: string, payload?: unknown, timeoutMs?: number): Promise<T>;
    request<T = unknown>(name: string, payload?: unknown, timeoutMs?: number): Promise<T>;
    on<T = unknown>(name: string, handler: (payload: T) => void): () => void;
    readonly state: GameState;
    readonly bind: BindingApi;
    readonly i18n: BannerlordHtmlUiI18n;
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
    readonly lifecycle: LifecycleApi;
    readonly errors: ErrorApi;
    readonly pages: BannerlordHtmlUiScopePages;
    readonly input: InputApi;
    readonly i18n: BannerlordHtmlUiI18n;
    ready(): Promise<Record<string, unknown>>;
  }
}

declare global {
  interface Window {
    game: BannerlordHtmlUI.WindowApi;
  }
}

export {};
