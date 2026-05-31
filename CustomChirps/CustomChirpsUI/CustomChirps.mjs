const React = window.React;
const Api = window["cs2/api"];

const LargeChirpPrefix = "customchirps:large:";
const PortraitImageChirpPrefix = "customchirps:portraitimg:";
const LargeChirpClass = "customchirps-large-chirp";
const UiModsPrefix = "coui://ui-mods/";
const LegacySocialTripsPortraitPrefix = `${UiModsPrefix}Portraits/`;
const SocialTripsPortraitPrefix = `${UiModsPrefix}SocialTrips/Portraits/`;
const ImageSourcesBinding = Api?.bindValue?.("customChirps", "imageSources", []);

function joinClasses(...classes) {
    return classes.filter(Boolean).join(" ");
}

function pushUnique(values, value) {
    if (value && !values.includes(value)) {
        values.push(value);
    }
}

function isAbsoluteImageSource(source) {
    return /^[a-z][a-z0-9+.-]*:\/\//i.test(source) ||
        source.startsWith("data:") ||
        source.startsWith("Media/");
}

function isRegisteredImageToken(source) {
    return /^img_[a-f0-9]{32}$/i.test(source);
}

function useImageSourceEntries() {
    if (Api?.useValue && ImageSourcesBinding) {
        return Api.useValue(ImageSourcesBinding) || [];
    }

    return [];
}

function resolveImageSource(source, entries) {
    const trimmed = typeof source === "string" ? source.trim() : "";
    if (!trimmed) {
        return { source: null, pending: false };
    }

    if (!isRegisteredImageToken(trimmed)) {
        return { source: trimmed, pending: false };
    }

    if (Array.isArray(entries)) {
        const match = entries.find((entry) => entry?.token === trimmed);
        const registeredSource = typeof match?.source === "string" ? match.source.trim() : "";
        if (registeredSource) {
            return { source: registeredSource, pending: false };
        }
    }

    return { source: null, pending: true };
}

function getImageSourceCandidates(source) {
    const trimmed = typeof source === "string" ? source.trim() : "";
    const candidates = [];
    if (!trimmed) {
        return candidates;
    }

    pushUnique(candidates, trimmed);

    if (trimmed.startsWith(LegacySocialTripsPortraitPrefix)) {
        pushUnique(
            candidates,
            trimmed.replace(LegacySocialTripsPortraitPrefix, SocialTripsPortraitPrefix)
        );
    }

    if (trimmed.startsWith("Portraits/")) {
        pushUnique(candidates, `${SocialTripsPortraitPrefix}${trimmed.slice("Portraits/".length)}`);
    }

    if (!isAbsoluteImageSource(trimmed)) {
        pushUnique(candidates, `${UiModsPrefix}${trimmed.replace(/^\/+/, "")}`);
    }

    return candidates;
}

function setImageSourceWithFallback(image, source) {
    const candidates = getImageSourceCandidates(source);
    let candidateIndex = 0;

    image.removeAttribute("data-customchirps-load-error");
    image.onload = () => {
        image.removeAttribute("data-customchirps-load-error");
    };
    image.onerror = () => {
        candidateIndex++;
        if (candidateIndex < candidates.length) {
            image.src = candidates[candidateIndex];
            return;
        }

        image.setAttribute("data-customchirps-load-error", "true");
        console.warn("[CustomChirps] Failed to load chirp image source.", {
            source,
            candidates
        });
    };
    image.src = candidates[0] ?? "";
}

function isLargeCustomChirp(chirp) {
    return typeof chirp?.messageId === "string" &&
        (
            chirp.messageId.startsWith(LargeChirpPrefix) ||
            chirp.messageId.startsWith(PortraitImageChirpPrefix)
        );
}

function getImageInfo(chirp) {
    if (typeof chirp?.messageId !== "string") {
        return { source: null, layout: null };
    }

    if (!chirp.messageId.startsWith(PortraitImageChirpPrefix)) {
        return { source: null, layout: null };
    }

    const prefix = PortraitImageChirpPrefix;
    const rest = chirp.messageId.slice(prefix.length);
    const end = rest.indexOf(":");
    if (end <= 0) {
        return { source: null, layout: null };
    }

    try {
        return { source: decodeURIComponent(rest.slice(0, end)), layout: "portrait" };
    } catch (error) {
        console.error("[CustomChirps] Failed to decode chirp image source.", error);
        return { source: null, layout: null };
    }
}

function findMessage(root) {
    return root.querySelector(
        ".customchirps-large-chirp [class^='message_'], .customchirps-large-chirp [class*=' message_']"
    );
}

function unwrapPortraitRow(root) {
    const row = root.querySelector("[data-customchirps-portrait-row='true']");
    if (!(row instanceof HTMLElement) || !(row.parentElement instanceof HTMLElement)) {
        return;
    }

    const message = row.querySelector("[class^='message_'], [class*=' message_']");
    if (message instanceof HTMLElement) {
        row.parentElement.insertBefore(message, row);
    }

    row.remove();
}

function LargeChirpWrapper({ ChirpLayout, props, imageSource, imageLayout }) {
    const rootRef = React.useRef(null);
    const imageSourceEntries = useImageSourceEntries();
    const resolvedImage = resolveImageSource(imageSource, imageSourceEntries);

    React.useEffect(() => {
        const root = rootRef.current;
        if (!(root instanceof HTMLElement)) {
            return;
        }

        const existing = root.querySelector("[data-customchirps-image-node='true']");
        if (!resolvedImage.source) {
            existing?.remove();
            unwrapPortraitRow(root);
            if (resolvedImage.pending) {
                console.info("[CustomChirps] Waiting for registered chirp image source.", imageSource);
            }
            return;
        }

        const message = findMessage(root);
        if (!(message instanceof HTMLElement) || !(message.parentElement instanceof HTMLElement)) {
            console.error("[CustomChirps] Unable to find large chirp message area for image.");
            return;
        }

        const image = existing instanceof HTMLImageElement
            ? existing
            : document.createElement("img");

        image.setAttribute("data-customchirps-image-node", "true");
        image.setAttribute("width", imageLayout === "portrait" ? "84" : "64");
        image.setAttribute("height", imageLayout === "portrait" ? "84" : "64");
        image.className = `${LargeChirpClass}-portrait-image`;
        image.alt = "";
        image.decoding = "async";
        setImageSourceWithFallback(image, resolvedImage.source);

        let row = root.querySelector("[data-customchirps-portrait-row='true']");
        if (!(row instanceof HTMLElement)) {
            row = document.createElement("div");
            row.setAttribute("data-customchirps-portrait-row", "true");
            row.className = `${LargeChirpClass}-portrait-row`;
            message.parentElement.insertBefore(row, message);
        }

        if (image.parentElement !== row) {
            row.insertBefore(image, row.firstChild);
        }

        if (message.parentElement !== row) {
            row.appendChild(message);
        }
    }, [resolvedImage.source, resolvedImage.pending, imageSource, imageLayout, props.chirp?.messageId]);

    return React.createElement(
        "div",
        {
            ref: rootRef,
            className: `${LargeChirpClass}-shell`,
            "data-customchirps-large": "true",
            "data-customchirps-image": imageSource ? "true" : "false",
            "data-customchirps-layout": imageLayout || "none"
        },
        React.createElement(ChirpLayout, {
            ...props,
            className: joinClasses(props.className, LargeChirpClass)
        })
    );
}

export const register = (moduleRegistry) => {
    if (!React || !moduleRegistry?.extend) {
        console.error("[CustomChirps] Unable to register large chirp UI module.");
        return;
    }

    console.info("[CustomChirps] Large chirp UI module registered.");

    moduleRegistry.extend(
        "game-ui/game/components/chirper/items/chirp-layout.tsx",
        "ChirpLayout",
        (ChirpLayout) => (props) => {
            if (!isLargeCustomChirp(props.chirp)) {
                return React.createElement(ChirpLayout, props);
            }

            const imageInfo = getImageInfo(props.chirp);
            return React.createElement(LargeChirpWrapper, {
                ChirpLayout,
                props,
                imageSource: imageInfo.source,
                imageLayout: imageInfo.layout
            });
        }
    );
};

export const hasCSS = true;
export default register;
