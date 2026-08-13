# v0.22

## Framework API

- Added command/request unregister APIs.
- Added page unregister API for consumer Mod cleanup.

## Consumer test

- Added `HtmlUiConsumerTestMod`, a standalone consumer module example.
- Consumer uses its own `UI/` content root.
- Added F11/F12 test open/close flow.
- Added Command, Request/Response, Event and State acceptance tests.

## Runtime rule

- Consumer pages do not copy or load `runtime.js`; BannerlordHtmlUI injects the framework runtime automatically.
