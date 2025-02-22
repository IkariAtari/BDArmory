#!/usr/bin/env python3

# Standard library imports
import csv
from pathlib import Path

# Third party imports
import matplotlib.pyplot as plt
import numpy

tournamentFolders = sorted(list(dir for dir in ((Path(__file__).parent / "Logs").resolve().glob("Tournament*")) if dir.is_dir()))
tournamentDir = tournamentFolders[-1] if len(tournamentFolders) > 0 else Path('.')
with open(tournamentDir / "summary.csv", 'r') as f:
    data = list(csv.reader(f))
vessel_count = data.index([]) - 1
names = [data[row][0] for row in range(len(data) - vessel_count, len(data))]
scores = numpy.array([[float(v) for v in data[row][1:]] for row in range(len(data) - vessel_count, len(data))])
plt.figure(figsize=(8, 5), dpi=200)
plt.plot(scores.transpose(), linewidth=5)
plt.legend(names)
plt.show(block=True)
