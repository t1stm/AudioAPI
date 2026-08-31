export type Bitrate = 32 | 64 | 96 | 112 | 128 | 144 | 160 | 192 | 256 | 320;
export type Codec = 'Opus' | 'Vorbis' | 'FLAC' | 'MP3' | 'AAC';

export const codecs: Codec[] = ['Opus', 'Vorbis', 'FLAC', 'MP3', 'AAC'];
export const bitrates: Bitrate[] = [32, 64, 96, 112, 128, 144, 160, 192, 256, 320];

const initialBitrate: Bitrate = 112;
const initialCodec: Codec = 'Opus';

class Quality {
	bitrate = $state(initialBitrate);
	codec = $state(initialCodec);
}

export default new Quality();
