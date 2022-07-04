The transition to github's issue based project management is in progress. 
Until that is completed, this will remain the home of the project's low-priority backlog.

---

---

*Issues added to GitHub from top to bottom, new issues may exist only on GitHub
*

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
Research effect on deduplication of file updates made to container files
	Specifically zip and tar file types
	Does making an update rewrite the entire file in such a way that, despite most of the extracted data being the same, the data on the disk is altered substantially enough that there are no longer significant runs of exactly equal data needed to generate equivalent splits?
	Could offer an option configured for specific files/types that extracts the data, stores it, then dedupes it
Blob packing
	Ability to pack blobs together into a single file.
	Can be used to reduce total number of files on disk and also to compress data
	Potential packing heuristics
		Pack blobs not in a recent back
		Pack children of a parent blob type together
		Pack blobs together which result in the best compression
Support for using a destination without reading its index
	Index would always be written to destination so destination is complete
	When available, would be read from another source
		Ie. locally or in a cloud provider with cheap reads
	All backups using the destination would be required to use the alternate index
		If the index in the destination were ever used, the alternate index would be unusable
			Some way to detect this without actually reading the index?
				Modified date?
Support for preferring supplying data from a destination while backing up another destination
	Would allow already created blobs in a more commonly used destination to be supplied to a less commonly used destination
		Ie. Frequent backups to external hard drive, infrequent backups to backblaze
	Would support plain blobs and using the metadata tree from the supplying backup to reduce the need for file scans
	Diff tree calculation
		Old diff (intersection) tree for backup having current state C with destination A = C ?? A
		Calc for diff tree for backup having current state C with destination A and B supplying A = C ?? (A + B)
			This diff only applies to A, as A cannot supply B
				Bidirectional supply should be supported as an option
