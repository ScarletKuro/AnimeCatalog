const outsideClickRegistrations = new Map();
let nextOutsideClickRegistrationId = 1;

export function getItem(key) {
    return window.localStorage.getItem(key);
}

export function setItem(key, value) {
    window.localStorage.setItem(key, value);
}

export function removeItem(key) {
    window.localStorage.removeItem(key);
}

export function replaceUrl(url) {
    window.history.replaceState(null, "", url);
}

export function registerOutsideClick(element, dotNetRef, methodName) {
    const registrationId = nextOutsideClickRegistrationId++;
    const handler = (event) => {
        if (!element || element.contains(event.target)) {
            return;
        }

        dotNetRef.invokeMethodAsync(methodName);
    };

    outsideClickRegistrations.set(registrationId, { handler, dotNetRef });
    document.addEventListener("pointerdown", handler, true);
    return registrationId;
}

export function unregisterOutsideClick(registrationId) {
    const registration = outsideClickRegistrations.get(registrationId);
    if (!registration) {
        return;
    }

    document.removeEventListener("pointerdown", registration.handler, true);
    registration.dotNetRef.dispose();
    outsideClickRegistrations.delete(registrationId);
}

export function downloadFile(fileName, mimeType, content) {
    const blob = new Blob([content], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
}

export function showModalDialog(element) {
    if (!element || typeof element.showModal !== "function" || element.open) {
        return;
    }

    // The top layer sits above every stacking context, so no z-index tuning can be undone by
    // a sibling card that happens to come later in the DOM.
    element.showModal();
}

export function scrollElementIntoView(element) {
    if (!element) {
        return;
    }

    element.scrollIntoView({
        behavior: "smooth",
        block: "start",
        inline: "nearest"
    });
}

// Scrolls only the container, never the page: the option grid is a nested scroller, and
// element.scrollIntoView() would drag the whole document to it. getBoundingClientRect deltas
// are used instead of offsetTop so the container needs no positioning context of its own.
export function scrollSelectedOptionIntoView(container) {
    if (!container) {
        return;
    }

    const selected = container.querySelector('[aria-checked="true"]');
    if (!selected) {
        return;
    }

    const containerBox = container.getBoundingClientRect();
    const selectedBox = selected.getBoundingClientRect();

    container.scrollTop += (selectedBox.top - containerBox.top) - (containerBox.height - selectedBox.height) / 2;
}
