fx_version 'cerulean'
games { 'gta5', 'rdr3' }
rdr3_warning 'I acknowledge that this is a prerelease build of RedM, and I am aware my resources *will* become incompatible once RedM ships.'

name 'ticket-tool-link'
author 'Ticket Tool'
description 'Shows staff a ticket opener\'s in-game profile and bans in Ticket Tool'
version '1.0.0'
repository 'https://github.com/ticket-tool-app/integrations'

-- Server only: nothing is sent to game clients.
server_script 'dist/server.js'
