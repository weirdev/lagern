
with open("tree.txt") as treefile:
    lines = [a.strip() for a in treefile.readlines()]
lines = lines[1:]

trees = []
tree = []    
for line in lines:
    if line == "*":
        trees.append(tree)
        tree = []
    else:
        tree.append(line)
trees.append(tree)

for pos, tree in enumerate(trees):
    nodes = []
    node = []
    for line in tree:
        if line == "-":
            nodes.append(node)
            node = []
        else:
            node.append(line)
    trees[pos] = nodes
    
for tree in trees[:60]:
    keycount = [len(tree[0])+1]
    newkeycount = []
    line = [[]]
    print(tree[0])
    for node in tree[1:]:
        keycount[0] -= 1
        newkeycount.append(len(node)+1)
        line[-1].append(node)
        if keycount[0] == 0:
            del keycount[0]
            line.append([])
        if len(keycount) == 0:
            keycount = newkeycount
            newkeycount = []
            print(line[:-1])
            line = [[]]
    print()
    