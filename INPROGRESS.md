I intend to transition to githubs issue based project management eventually. 
For now this will remain the home of the project backlog.

====
Need to cleanup usage/meaning of shallow suffix in ref counts
Replace suffix with a struct with separate flag for shallow

===
Currently working on multiple backup destinations
Multiple destinations may be missing different blobs existing in the current backup
	If just one previous tree used to generate delta tree, we may not scan (and thus generate blobs for) a file that is in the previous selected tree but not in another destination. Thus we need to get blobs to the destination lacking them.
	We dont want to do a general backup sync between destinations, because we may not want all backups on all destinations (ie. frequent backups to disk/local storage, infrequent backups to cloud)
	Intersection tree--calculate tree of files/dirs in common between all destinations
		Use this for delta tree => extra files get scanned, but all blobs get where they need to go with minimal code changes
	Problem: Adding new destination (or effectively doing so by changing a bunch of data, backing up to D1, then to D1 and D2 together) incurs the same runtime as a brand new backup to D2 as every file gets scanned
		Possible mitigation: Clone existing backupset/transfer to new backup set then run backup
====

*Issues added to GitHub from top to bottom, new issues may exist only on GitHub
*

Add ignore rule for "only update file once per day/week/month..."
Better support for large backup sets
	Optimize B+ tree BlobStore
		optimize node size
			Make BlobLocations a set size?
				BlobLocations can currently hold variable length hash lists
					Need recursive hash lists forming binary or b-tree?
						Use this structure to store all references (ie files and dirs in mdtrees)?
					Or store hash lists as blobs?
		store some nodes out of memory?
	Test large backups
		Identify bottlenecks
Data integrity and encryption support
	Channel codes for backupset and blobstore files
	Channel codes for blobs?
		Make optional?
		blob hash would have to be calculated from (blobdata + redundant bits) so entire file can easily be hash checked when using online services
	Verify on write?
		Backing up
		Restoring
Project structure change
	Better API compliance
		Public api only in Core
	ICoreSrcDependencies and ICoreDstDependencies become IBackupSourceDependencies and IBackupDestinationDependencies
	Remove Core and split its API between new classes BackupSource and BackupDestination as approperiate
		BackupSource may have list of destinations it backs up to
Delete HashBlobPair class?
	Replace with tuples
MetadataNode
	Improve semantics of serializing/deserializing
		Better handling of difference between loading the entire tree into memory and loading single node
Backblaze support
	Single queue for pending transmissions?
	Ability to stall main thread from queuing more uploads?
		Need to limit number of file blocks loaded into memory?
			Dont worry about this for now but definitely test at some point
			SemaphoreSlim class to limit number of files being uploaded at a time?
	If receive Retry-After header, use specified time
		Currently not bothering
	init backblaze -n test -c C:\Users\Wesley\Desktop\test\cache --cloud-config C:\Users\Wesley\Desktop\test\src\BBConnection.json
Replace custom settings file format with YAML
	Rework how readsetting, etc are called
Add more unit tests
	Verify more conditions in existing unit tests
"Enhanced data"
	NTFS Permissions support
		Ability to escalate this application's own permissions
			Only when needed
	NTFS extended attributes
	Ability to save/restore enhanced or "dumb" data
		Save/restore w/ & w/o permissions
			Detect restore to machine without user/group corresponding to permissions being applied
		Save/restore w/ & w/o extended attributes
	Special link support
	Generic permissions support ie. Linux (POSIX)
		Handle restoring to different OS/permissions scheme than saved to
Support deleting entire backup sets (at dest)
Handle common things that could go wrong
	better handling when reading a file fails
		after failure, option to ignore file in .track file
	circular links when checking if ancestor is backup source
	safe writeout of indexes
		Write out old index without overwriting then rename
	when only one of backup or blob index is updated
		more generally any time they become out of sync
			"stamp" them with uuid so can detect out of sync?
	Save "progress" so can pick up after large backup halted
	more...
ArgParser
	"-l <>" => "-l <longname>"
		wont need Item3 of returned tuple
		create mapping from short to longnames
			only use longnames to access parsed data
	can require only one or at least one of a set of options
		'|' or '^' between options
		options inside [] ?
Replace ArgParser with Knack?
	https://github.com/Microsoft/knack
More flexible "list" command
	Ranges of n-m backups ago
		Also by date?
	Better handling of browsing list of many backups
Consistency check for MetadataNode
	When path and FileMetadata specified path should either always or never contain file/dirname
	GetFile and GetDirectory should use the same strategy
	Add tests
Replicate source file tree (as last backed up) in destination
	BackupLocations point at these files
	Block in the backup store but not in the replicated tree stored in a seperate folder
	Easy step to a versioning system
		Data would have to be preduplicated to detect changes
			Unless used through a virtual filesystem
			Or could be used in tandem with a backup disk
				without backup disk would have access to all changes except the state of the latest backup
Reverse references in BlobStore?
	Every blob knows hash of every structure that points at it?
	Could list every backup containing file
	Use worth the complexity?
Switch model classes to record types