export const clamp = (value, min, max) => Math.min(Math.max(value, min), max);

const INTEGER = /^[+-]?\d+$/;

// Returns total seconds (>= 30) for "mm:ss" or plain seconds, otherwise null.
export function tryParseDuration(input) {
    const trimmed = input.trim();
    if (!trimmed) {
        return null;
    }

    if (trimmed.includes(':')) {
        const parts = trimmed.split(':').map((part) => part.trim()).filter(Boolean);
        if (parts.length === 2 && INTEGER.test(parts[0]) && INTEGER.test(parts[1])) {
            const minutes = Number(parts[0]);
            const seconds = Number(parts[1]);
            if (seconds >= 0 && seconds < 60) {
                const totalSeconds = minutes * 60 + seconds;
                return totalSeconds >= 30 ? totalSeconds : null;
            }
        }
    }

    if (INTEGER.test(trimmed)) {
        const totalSeconds = Number(trimmed);
        return totalSeconds >= 30 ? totalSeconds : null;
    }

    return null;
}

export function formatDuration(totalSeconds) {
    const safe = Math.max(0, Math.floor(totalSeconds));
    const minutes = String(Math.floor(safe / 60)).padStart(2, '0');
    const seconds = String(safe % 60).padStart(2, '0');
    return `${minutes}:${seconds}`;
}

export const isValidHexColor = (value) =>
    typeof value === 'string' && /^#(?:[0-9a-f]{3}|[0-9a-f]{6}|[0-9a-f]{8})$/i.test(value);
