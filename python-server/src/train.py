
import torch
from torch.utils.data import DataLoader, random_split
import torch.optim as optim
import torch.nn.functional as F
from dataset import EmotionDataset
from model import CNNAudio

dataset = EmotionDataset("../data")
n_classes = len(dataset.label_map)

train_loader = DataLoader(dataset, batch_size=4, shuffle=True)

device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
model = CNNAudio(n_classes).to(device)
optimizer = optim.Adam(model.parameters(), lr=1e-3)

for epoch in range(5):
    model.train()
    for x, y in train_loader:
        x = x.to(device)
        y = y.to(device)
        optimizer.zero_grad()
        out = model(x)
        loss = F.cross_entropy(out, y)
        loss.backward()
        optimizer.step()
    print(f"Epoch {epoch+1} complete")

torch.save({
    "model_state_dict": model.state_dict(),
    "label_map": dataset.label_map
}, "../models/cnn_mfcc.pth")
print("Model saved.")
