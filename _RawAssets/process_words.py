words = None
letters = []

WORD_LENGTH_MULTIPLIER = 1.2 # for each letter > 3, multiply score by this.
SCORE_MULTIPLIER = 1 # multiplier to inflate all scores equally
SCORE_BASE = 50 # base to add to every letter score before any multiplier

with open("words_alpha.txt") as f:
  words = f.read().split("\n")

with open("letters.csv") as f:
  lines = f.read().split("\n")[1:]
  for line in lines:
    letters.append(line.split(","))
  
final_words = []
points = []

for word in words:
  if len(word) <= 2:
    continue
  if "q" in word and "qu" not in word:
    continue

  final_words.append(word)
  score = 0

  for letter in word:
    letter_line = letters[ord(letter) - ord("a")]
    score += int(letter_line[2]) + SCORE_BASE
  
  score *= SCORE_MULTIPLIER
  points.append(round(score * (WORD_LENGTH_MULTIPLIER ** (len(word) - 3))))

assert len(final_words) == len(points)

print("First 20 words:")
for i in range(20):
  print(final_words[i], "\t\t", points[i])

with open("words.csv", "w") as f:
  f.write("Word,Definition,Points\n")
  for i in range(len(final_words)):
    f.write(f"{final_words[i]},,{points[i]}\n")