// Builds the Windows voice-follow helper (native/win-speech) into build/win-speech.
// Needs the .NET 10 SDK. Skips work when the exe is newer than its sources.
// Off Windows it does nothing unless --force is passed (used by dist:win).
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..');
const projectDir = path.join(root, 'native', 'win-speech');
const outputDir = path.join(root, 'build', 'win-speech');
const exe = path.join(outputDir, 'WinSpeechHelper.exe');
const force = process.argv.includes('--force');

if (process.platform !== 'win32' && !force) {
    process.exit(0);
}

const sources = fs.readdirSync(projectDir)
    .filter((name) => name.endsWith('.cs') || name.endsWith('.csproj'))
    .map((name) => fs.statSync(path.join(projectDir, name)).mtimeMs);

if (fs.existsSync(exe) && fs.statSync(exe).mtimeMs > Math.max(...sources)) {
    process.exit(0);
}

console.log('Building the Windows speech helper...');
const result = spawnSync('dotnet', ['publish', '-c', 'Release', '-o', outputDir], {
    cwd: projectDir,
    stdio: 'inherit',
    shell: process.platform === 'win32'
});

if (result.error || result.status !== 0) {
    console.error('Could not build the Windows speech helper. Install the .NET 10 SDK, or voice-follow will fall back to Vosk.');
    process.exit(force ? 1 : 0);
}
