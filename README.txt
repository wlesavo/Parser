Simple parser for a small project, which was aimed to take popular new posts from vk, instagram and youtube of a particular comunity into a single database.
As a temporary solution for testing mobile app this storage was also backed up to a json file, hosted on a ftp server. 
This app lused tokens provided by different APIs to collect all recent posts. Then it would parse hashtags, likes/views counts, titles and other info and also provide a simple threshold check if the post is popular or not. If it is, the app would store all the info and send a push notification to the mobile app users.

App was hosted on a free platform "appharbor.com" as a background worker. 
It was a testing prototype with *a lot* of weak/dangerous places and was never supposed to see the light of production, at least in the form it was.

2018
