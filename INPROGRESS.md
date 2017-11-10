I intend to transition to githubs issue based project management eventually. 
For now this will remain the home of the project backlog.

====

Better support for large backup sets
	Optimize Split data
	Optimize B+ tree BlobStore
		bulk loading of tree
		store some nodes out of memory?
		optimize node size
		Progress report/bar
	Reduce number of blob files per directory
		Currently one file per blob and all files in destination root
		(Some) operating systems have poor performance with many files in a single directory
Add more unit tests
	detecting backups and dereferencing
Locking for destinations seperated from their caches
	Prevent deleting (adding?) backups without cache
		Cache may contain references in its backups to data no longer in destination
	Browsing and restoring from destination without cache still allowed
	Not true locking but display warnings
		Require "force" parameter to be passed to continue
	Only lock/warn once
		After warned operation performed at dst
			Cache may have bad references
			Have some kind of safe way of attempting to integrate cache
				Otherwise need to clear and reinitialize cache
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