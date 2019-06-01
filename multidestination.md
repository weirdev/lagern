Selected design = Solution 1: Intersection tree--calculate tree of files/dirs in common between all destinations

Design Considerations:
Multiple destinations may be missing different blobs existing in the current backup
	If just one previous tree used to generate delta tree, we may not scan (and thus generate blobs for) a file that is in the previous selected tree but not in another destination. Thus we need to get blobs to the destination lacking them.
	We dont want to do a general backup sync between destinations, because we may not want all backups on all destinations (ie. frequent backups to disk/local storage, infrequent backups to cloud)
	Solution 1:
		Intersection tree--calculate tree of files/dirs in common between all destinations
			Use this for delta tree => extra files get scanned, but all blobs get where they need to go with minimal code changes
		Problem: Adding new destination (or effectively doing so by changing a bunch of data, backing up to D1, then to D1 and D2 together) incurs the same runtime as a brand new backup to D2 as every file gets scanned
			Possible mitigation: Clone existing backupset/transfer to new backup set then run backup
	Solution 2:
		Union tree--calculate tree of files/dirs present in any destination
			Use this for delta tree => Need to push out missing blobs from whichever destination they currently reside
			Problem: expensive to retrieve blobs from some destinations, makes backup operation far more complex
	Solution 3:
		Intersection across destinations of union tree within destinations
			Same drawbacks as Solution 1 but catches files stored in previous backups but not this one
				Probably dont want this, appearing/disappearing/appearing is probably a signal that scanning the file is a good idea
				Also adds a lot of complicaiton to the code
	Solution 4:
		Select one tree, take intersection of blob index
		We know when we are storing a blob a backup lacks