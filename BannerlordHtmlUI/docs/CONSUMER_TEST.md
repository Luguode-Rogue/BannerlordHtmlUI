# Consumer Mod Acceptance Test

`examples/HtmlUiConsumerTestMod` is the canonical first real consumer test.

## Expected flow

```text
HtmlUiConsumerTestMod
        |
        +-- RegisterContentRoot("HtmlUiConsumerTestMod", UI/)
        |
        +-- Register Page
        |
        +-- Register Command
        |
        +-- Register Request
        |
        +-- Publish State
        |
        v
BannerlordHtmlUI
        |
        v
WebView2
        |
        v
HTML/CSS/JS
```

## Test keys

- F11: open the consumer page
- F12: close the consumer page

## Acceptance

The test is passed when:

1. F11 opens the consumer page.
2. `Framework runtime ready` appears.
3. `consumer.loaded` becomes true.
4. Increment causes `consumer.counter` and `consumer.counterChanged` to change.
5. Request returns the Mod name, echo and counter.
6. Capture/Release buttons change input behavior without killing the page.
7. F12 closes the page and returns input to Bannerlord.
8. Exiting Bannerlord leaves no consumer WebView/overlay behind.
