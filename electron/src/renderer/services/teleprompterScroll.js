// Viewport contract (IScrollViewport): { currentOffset, maxOffset, scrollToOffset(offset) }.
export class TeleprompterScrollService {
    constructor() {
        this.viewport = null;
        this.activeMode = 'manual';
        this.manualSpeedPixelsPerSecond = 54;
        this.timedDurationMs = 180_000;
        this.onPlaybackStateChanged = () => {};
        this.frameHandle = null;
        this.lastTick = 0;
        this.timedStarted = 0;
        this.timedStartOffset = 0;
        this.timedTargetOffset = 0;
    }

    get isRunning() {
        return this.frameHandle !== null;
    }

    attachViewport(viewport) {
        this.viewport = viewport;
    }

    start(mode) {
        if (!this.viewport) {
            return;
        }

        this.activeMode = mode;
        this.lastTick = performance.now();

        if (mode === 'timed') {
            this.timedStarted = this.lastTick;
            this.timedStartOffset = this.viewport.currentOffset;
            this.timedTargetOffset = this.viewport.maxOffset;
        }

        if (this.frameHandle === null) {
            this.frameHandle = requestAnimationFrame(this.tick);
        }

        this.onPlaybackStateChanged();
    }

    pause() {
        if (this.frameHandle === null) {
            return;
        }

        cancelAnimationFrame(this.frameHandle);
        this.frameHandle = null;
        this.onPlaybackStateChanged();
    }

    toggle(mode) {
        if (this.isRunning && this.activeMode === mode) {
            this.pause();
            return;
        }

        this.start(mode);
    }

    restartFromTop() {
        this.viewport?.scrollToOffset(0);
        if (this.activeMode === 'timed' && this.isRunning) {
            this.timedStarted = performance.now();
            this.timedStartOffset = 0;
            this.timedTargetOffset = this.viewport?.maxOffset ?? 0;
        }
    }

    tick = (now) => {
        this.frameHandle = requestAnimationFrame(this.tick);

        if (!this.viewport) {
            this.pause();
            return;
        }

        const elapsedSeconds = Math.max(0.001, (now - this.lastTick) / 1000);
        this.lastTick = now;

        switch (this.activeMode) {
            case 'manual': {
                this.viewport.scrollToOffset(this.viewport.currentOffset + this.manualSpeedPixelsPerSecond * elapsedSeconds);
                if (this.viewport.currentOffset >= this.viewport.maxOffset) {
                    this.pause();
                }

                break;
            }

            case 'timed': {
                if (this.timedDurationMs <= 0) {
                    this.pause();
                    return;
                }

                const progress = Math.min(Math.max((now - this.timedStarted) / this.timedDurationMs, 0), 1);
                this.viewport.scrollToOffset(this.timedStartOffset + (this.timedTargetOffset - this.timedStartOffset) * progress);
                if (progress >= 1) {
                    this.pause();
                }

                break;
            }

            default:
                this.pause();
                break;
        }
    };
}
