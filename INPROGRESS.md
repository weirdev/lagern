I intend to transition to githubs issue based project management eventually. 
For now this will remain the home of the project backlog.

====

Code cleanup
Add status command
	much like git status
	would show differences and estimate size of a run
	also show path to src/dst
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
Support a cache
	Warn when deleting backups from destination when the cache is not present
		Cache depends on metadatatrees from previous backups for differential backups
			especially last backup (default)
	Warn when trying to sync cache but cache and destination aren't both available
	Eventually implement blob-level cache?
Add more unit tests
	deteting backups and dereferencing
Support deleting entire backup stores (at dest)
Test support for large backup sets
	Optimize B+ tree BlobStore
		bulk loading of tree
		store some nodes out of memory?
		optimize node size
		Progress report/bar
	Reduce number of blob files per directory
		Currently one file per blob and all files in destination root
		(Some) operating systems have poor performance with many files in a single directory
Handle common things that could go wrong
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
More flexible "list" command
	Ranges of n-m backups ago
		Also by date?
	Better handling of browsing list of many backups
.Net Core port
	Begin with .Net standard port of library code
		.Net standard library will work with Framework and Code versions of project
	Make primary instance of project?
	Test under linux
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