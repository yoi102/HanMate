# Engineering audio fixtures

Original synthetic 440 Hz sine signals, not human voices or teaching pronunciation.
Generated locally with FFmpeg 7.1 from imageio-ffmpeg 0.6.0, without external source audio.
The FFmpeg executable is not bundled with the application or these fixtures.

```
ffmpeg -f lavfi -i sine=frequency=440:duration=1:sample_rate=44100 -c:a libmp3lame -b:a 64k tone.mp3
ffmpeg -f lavfi -i sine=frequency=440:duration=1:sample_rate=44100 -c:a aac -b:a 64k tone.m4a
```

The MP3 and AAC-LC containers are used for real native decoding and malformed-input
tests. They do not count toward formal pronunciation coverage.
