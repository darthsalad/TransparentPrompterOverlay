const SILENCE_CHECK_MS = 350;
const SILENCE_THRESHOLD_MS = 1200;

// Vosk.createModel never settles when the worker reports an error, so listen for both events.
function createVoskModel(url) {
    return new Promise((resolve, reject) => {
        const model = new window.Vosk.Model(url, -1);
        model.on('load', (message) => message.result ? resolve(model) : reject(new Error('the speech model failed to load')));
        model.on('error', (message) => {
            model.terminate();
            reject(new Error(String(message.error)));
        });
    });
}

// Offline recognition with Vosk (WebAssembly). Chromium's Web Speech API needs Google's
// cloud service and does not work inside Electron, so it is not an option here.
export class SpeechFollowService {
    constructor(alignmentService, { ensureModel, onModelProgress }) {
        this.alignmentService = alignmentService;
        this.ensureModel = ensureModel;
        this.onFollowUpdate = () => {};
        this.onStatusChanged = () => {};
        this.isListening = false;
        this.currentCharacterIndex = 0;
        this.lastSpeech = 0;
        this.reportedSpeaking = false;
        this.lastPartial = '';
        this.model = null;
        this.modelLoading = null;
        this.recognizer = null;
        this.audio = null;
        this.silenceTimer = null;
        this.startToken = 0;

        onModelProgress(({ receivedBytes, totalBytes }) => {
            const percent = totalBytes > 0 ? ` ${Math.round((receivedBytes / totalBytes) * 100)}%` : '';
            this.onStatusChanged(`Downloading the offline speech model (one-time, about 40 MB)...${percent}`);
        });
    }

    updateScript(script) {
        this.alignmentService.updateScript(script);
    }

    async start(currentCharacterIndex) {
        this.currentCharacterIndex = currentCharacterIndex;
        if (this.isListening) {
            return;
        }

        const token = ++this.startToken;
        this.isListening = true;

        try {
            const model = await this.loadModel();
            const stream = await navigator.mediaDevices.getUserMedia({
                video: false,
                audio: { echoCancellation: true, noiseSuppression: true, channelCount: 1 }
            });

            if (token !== this.startToken) {
                stream.getTracks().forEach((track) => track.stop());
                return;
            }

            const context = new AudioContext();
            const recognizer = new model.KaldiRecognizer(context.sampleRate);
            recognizer.on('result', ({ result }) => {
                this.lastPartial = '';
                if (result?.text) {
                    this.publishTranscript(result.text);
                }
            });
            recognizer.on('partialresult', ({ result }) => {
                const partial = result?.partial ?? '';
                if (partial && partial !== this.lastPartial) {
                    this.lastPartial = partial;
                    this.publishTranscript(partial);
                }
            });

            const source = context.createMediaStreamSource(stream);
            const processor = context.createScriptProcessor(4096, 1, 1);
            processor.onaudioprocess = (event) => {
                try {
                    recognizer.acceptWaveform(event.inputBuffer);
                } catch (error) {
                    this.onStatusChanged(`Voice-follow stopped: ${error.message}`);
                }
            };
            source.connect(processor);
            processor.connect(context.destination);

            this.recognizer = recognizer;
            this.audio = { stream, context, source, processor };
            this.lastSpeech = performance.now();
            this.silenceTimer = setInterval(this.checkSilence, SILENCE_CHECK_MS);
            this.onStatusChanged('Voice-follow is listening. Read naturally and the overlay will try to keep your current line centered.');
        } catch (error) {
            if (token === this.startToken) {
                this.isListening = false;
                this.onStatusChanged(`Voice-follow could not start: ${error.message ?? error}`);
            }
        }
    }

    async stop() {
        if (!this.isListening) {
            return;
        }

        this.startToken++;
        this.isListening = false;
        this.reportedSpeaking = false;
        this.lastPartial = '';
        clearInterval(this.silenceTimer);
        this.silenceTimer = null;

        if (this.audio) {
            const { stream, context, source, processor } = this.audio;
            processor.onaudioprocess = null;
            source.disconnect();
            processor.disconnect();
            stream.getTracks().forEach((track) => track.stop());
            await context.close().catch(() => {});
            this.audio = null;
        }

        this.recognizer?.remove();
        this.recognizer = null;

        this.onFollowUpdate({ isSpeaking: false, transcriptFragment: '', alignment: null });
        this.onStatusChanged('Voice-follow is paused.');
    }

    async loadModel() {
        if (this.model) {
            return this.model;
        }

        this.modelLoading ??= (async () => {
            this.onStatusChanged('Preparing the offline speech model...');
            const { url, name } = await this.ensureModel();
            this.onStatusChanged('Loading the offline speech model...');
            const model = await createVoskModel(url);
            this.model = model;
            this.onStatusChanged(`Voice-follow is using ${name.replace(/\.tar\.gz$/, '')}.`);
            return model;
        })().finally(() => {
            this.modelLoading = null;
        });

        return this.modelLoading;
    }

    publishTranscript(text) {
        this.lastSpeech = performance.now();
        this.reportedSpeaking = true;

        const alignment = this.alignmentService.tryAlign(text, this.currentCharacterIndex);
        if (alignment) {
            this.currentCharacterIndex = alignment.characterIndex;
        }

        this.onFollowUpdate({ isSpeaking: true, transcriptFragment: text, alignment });
    }

    checkSilence = () => {
        if (!this.isListening || !this.reportedSpeaking) {
            return;
        }

        if (performance.now() - this.lastSpeech < SILENCE_THRESHOLD_MS) {
            return;
        }

        this.reportedSpeaking = false;
        this.onFollowUpdate({ isSpeaking: false, transcriptFragment: '', alignment: null });
    };
}
