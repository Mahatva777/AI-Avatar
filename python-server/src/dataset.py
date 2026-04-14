
import os
import librosa
import numpy as np
from torch.utils.data import Dataset

def extract_mfcc(wav, sr=16000, n_mfcc=40):
    mfcc = librosa.feature.mfcc(y=wav, sr=sr, n_mfcc=n_mfcc)
    mfcc = (mfcc - np.mean(mfcc)) / (np.std(mfcc) + 1e-6)
    return mfcc.astype(np.float32)

class EmotionDataset(Dataset):
    def __init__(self, root_dir, max_len=128):
        self.samples = []
        self.labels = []
        self.label_map = {}
        self.max_len = max_len

        classes = sorted(os.listdir(root_dir))
        self.label_map = {c:i for i,c in enumerate(classes)}

        for c in classes:
            folder = os.path.join(root_dir, c)
            for f in os.listdir(folder):
                if f.endswith(".wav"):
                    self.samples.append(os.path.join(folder, f))
                    self.labels.append(self.label_map[c])

    def __len__(self):
        return len(self.samples)

    def __getitem__(self, idx):
        wav, sr = librosa.load(self.samples[idx], sr=16000)
        mfcc = extract_mfcc(wav, sr)

        if mfcc.shape[1] < self.max_len:
            pad = self.max_len - mfcc.shape[1]
            mfcc = np.pad(mfcc, ((0,0),(0,pad)))
        else:
            mfcc = mfcc[:, :self.max_len]

        mfcc = np.expand_dims(mfcc, 0)
        return mfcc, self.labels[idx]
