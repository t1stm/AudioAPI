type Bitrate = 32 | 64 | 96 | 112 | 128 | 144 | 160 | 192 | 256 | 320;
type Codec = 'Opus' | 'Vorbis' | 'FLAC' | 'MP3' | 'AAC';

const initialBitrate: Bitrate = 112;
const initialCodec: Codec = 'Opus';

class Quality {
	bitrate = $state(initialBitrate);
	codec = $state(initialCodec);
}

export default new Quality();
