const { contextBridge, ipcRenderer } = require('electron');

function subscribe(channel, callback) {
    ipcRenderer.on(channel, (_event, ...args) => callback(...args));
}

contextBridge.exposeInMainWorld('prompter', {
    loadSettings: () => ipcRenderer.invoke('settings:load'),
    saveSettings: (settings) => ipcRenderer.send('settings:save', settings),
    setWindowSize: (width, height) => ipcRenderer.send('window:set-size', width, height),
    setMinimal: (enabled) => ipcRenderer.send('window:set-minimal', enabled),
    setContentProtection: (enabled) => ipcRenderer.send('window:set-content-protection', enabled),
    setClickThrough: (enabled) => ipcRenderer.send('window:set-click-through', enabled),
    toggleVisibility: () => ipcRenderer.send('window:toggle-visibility'),
    closeWindow: () => ipcRenderer.send('window:close'),
    ensureSpeechModel: () => ipcRenderer.invoke('speech:ensure-model'),
    nativeSpeechAvailable: () => ipcRenderer.invoke('speech:native-available'),
    startNativeSpeech: () => ipcRenderer.send('speech:native-start'),
    stopNativeSpeech: () => ipcRenderer.send('speech:native-stop'),
    onNativeSpeechEvent: (callback) => subscribe('speech:native-event', callback),
    onHotkey: (callback) => subscribe('hotkey', callback),
    onWindowBounds: (callback) => subscribe('window:bounds', callback),
    onWindowShown: (callback) => subscribe('window:shown', callback),
    onSpeechModelProgress: (callback) => subscribe('speech:model-progress', callback)
});
