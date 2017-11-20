1. Create and publish feature branch
2. Do work
3. Commit and push when are at a reasonable stopping point
	If last commit(s) were temporary:
		`git reset --soft <last non temporary commit>`
		Commit and push like normal
	Project must build and all tests passing prior to commit must still pass (unless tests added)
3. When stopping between commits, make and push a temporary commit
	First line: "Temporary commit for <featurebranch> work."
	Second line blank, third line lists what has been done since last commit
		This may be more detailed than a standard commit and provide context to help pick up where work stopped
	Can make multiple temporary commits, dont amend after pushing